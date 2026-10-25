using GHPC.Weapons;
using MelonLoader;
using UnityEngine;

namespace M1A1Abrams
{
    /// <summary>
    /// M2 供弹链。
    ///
    /// 以模型为准：链节（LINK_xx）的位置、朝向、蒙皮权重全部保持模型原样。
    /// 代码只驱动两件事，其余一律不动。
    ///
    ///   1) 步进 —— 每发子弹沿弹链路径往前滑一格，滑满一格无缝接回起点；
    ///   2) 俯仰 —— 靠近枪机的一端跟着枪管转，靠近弹药箱的一端钉死。
    ///
    /// 为什么不用模型自带的 BeltStepOne 片段：
    ///   桥里实测（AnimationClip.SampleAnimation / Animation+legacy 两种方式都试过）
    ///   打进 AssetBundle 之后，那段片段的曲线一个数值都写不到骨骼上 ——
    ///   clip 本身 len=0.0909、legacy=False、empty=False 看着正常，但采样完全没反应。
    ///   与其跟打包管线较劲，不如直接按几何关系驱动骨骼：链节位置模型里就有，
    ///   相邻两节之间就是"一格"，把子弹放在这两点之间插值即可，效果和片段一致，
    ///   而且不依赖任何被裁掉的动画数据。
    /// </summary>
    internal sealed class CrowsBeltRig : MonoBehaviour
    {
        // M2HB 实际射速 550 rpm → 一格 0.1091 秒
        private const float GunRpm = 550f;

        private Transform _rig;      // BELT_RIG
        private Transform _cradle;   // GUN
        private Transform _muzzle;   // GUN VIS/MUZZLE
        private Transform[] _links;  // LINK_01..LINK_34
        private Transform[] _adv;    // LINK_kk/ADV_kk，子弹骨骼
        private float[] _weight;

        // 基准：BELT_RIG 局部空间
        private Vector3[] _restPos;
        private Quaternion[] _restRot;
        private Vector3[] _restGunLocal; // 每节在枪机局部空间里的基准偏移

        private WeaponSystem _weapon;
        private bool _eventBound;

        private float _phase;         // 已经走过多少格（连续值）
        private float _phaseTarget;   // 已经打掉多少发
        private float _stepSeconds;

        private Vector3 _restBore;
        private Quaternion _restGunRot;
        private Vector3 _axis;
        private bool _ready;
        private bool _warned;

        public void Bind(Transform cradle, WeaponSystem weapon)
        {
            _cradle = cradle;
            _rig = ModUtil.Transforms.FindDeep(transform, "BELT_RIG");
            if (_rig == null) { Warn("BELT_RIG not found"); return; }

            _muzzle = ModUtil.Transforms.FindDeep(_cradle, "MUZZLE");

            var links = new System.Collections.Generic.List<Transform>();
            for (int i = 1; i <= 64; i++)
            {
                var t = _rig.Find("LINK_" + i.ToString("D2"));
                if (t == null) break;
                links.Add(t);
            }
            if (links.Count < 2) { Warn("LINK_xx not found under BELT_RIG"); return; }
            _links = links.ToArray();

            int n = _links.Length;
            _adv = new Transform[n];
            for (int k = 0; k < n; k++)
            {
                var a = _links[k].Find("ADV_" + (k + 1).ToString("D2"));
                // 保底：名字对不上就取第一个 ADV 子节点
                if (a == null)
                {
                    for (int i = 0; i < _links[k].childCount; i++)
                        if (_links[k].GetChild(i).name.StartsWith("ADV_")) { a = _links[k].GetChild(i); break; }
                }
                _adv[k] = a;
            }

            _weight = new float[n];
            for (int k = 0; k < n; k++)
            {
                // 靠近进弹口的 HEAD_PLATES 节强制 w=1：完全焊死在枪机上，不与后面插值。
                // 模型里这些链节每块都是单骨骼 100% 硬权重（实测 6802 顶点里
                // 多骨骼混合数 = 0），所以只要两块骨骼之间错开一点点，接缝就露缝；
                // 让头部整段同进同出，进弹口那里永远是严丝合缝的。
                const int HeadPlates = 4;
                if (k < HeadPlates) { _weight[k] = 1f; continue; }

                float u = (float)(k - HeadPlates + 1) / (float)(n - HeadPlates);
                u = Mathf.Clamp01(u);
                _weight[k] = 1f - (u * u * (3f - 2f * u));
            }

            // 动画状态机一律停掉：有 Animator 在场时任何采样都被吞掉，且我们不吃片段。
            var anim = GetComponentInChildren<Animator>(true);
            if (anim != null) Destroy(anim);

            // 蒙皮包围盒不会随骨骼更新，摆到一定角度就会被视锥剔除 —— 关掉剔除判断。
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
            }

            _stepSeconds = 60f / GunRpm;

            // 供弹信号：直接挂 WeaponSystem.Fired（Action<AmmoType, LiveRound>），一发一次。
            // 之前靠轮询 AmmoFeed._feedClipMain 的 Queue.Count 不可靠 ——
            // 连发时弹匣会把子弹补进队列，Count 一会儿掉一会儿涨，
            // 涨的那一帧被当成"换弹"把相位清零，表现就是点射有动画、长按完全不动。
            _weapon = weapon;
            if (_weapon != null)
            {
                _weapon.Fired += OnFired;
                _eventBound = true;
            }

            int advCount = 0;
            foreach (var a in _adv) if (a != null) advCount++;

            MelonLogger.Msg("[BELT] bound: links=" + n + " adv=" + advCount);
        }

        /// <summary>
        /// 一发一发推进相位。事件在 WeaponSystem 真正完成射击时触发，
        /// 连发、点射、切换弹种都能收到，不依赖任何会被补弹打乱的计数。
        /// </summary>
        private void OnFired(AmmoType ammo, LiveRound round)
        {
            _phaseTarget += 1f;
        }

        private void OnDestroy()
        {
            if (_eventBound && _weapon != null)
            {
                _weapon.Fired -= OnFired;
                _eventBound = false;
            }
        }

        private void LateUpdate()
        {
            if (_links == null) return;

            if (!_ready && !CaptureRest()) return;

            DriveFeed();

            ApplyElevation();

            StepBullets();

        }

        private bool CaptureRest()
        {
            var bore = WorldBore();
            if (bore == Vector3.zero)
            {
                if (!_warned) { _warned = true; Warn("MUZZLE not ready, retry next frame"); }
                return false;
            }

            int n = _links.Length;
            _restPos = new Vector3[n];
            _restRot = new Quaternion[n];
            _restGunLocal = new Vector3[n];

            for (int k = 0; k < n; k++)
            {
                _restPos[k] = _rig.InverseTransformPoint(_links[k].position);
                _restRot[k] = Quaternion.Inverse(_rig.rotation) * _links[k].rotation;
                // 每节在"枪机局部空间"里的基准偏移：这一份是纯位置偏移，
                // 用 Transform 的逆变换算，带负缩放镜像的父级也能算对。
                _restGunLocal[k] = _cradle.InverseTransformPoint(_links[k].position);
            }

            _restBore = _rig.InverseTransformDirection(bore).normalized;
            _restGunRot = Quaternion.Inverse(_rig.rotation) * _cradle.rotation;
            var side = Vector3.Cross(_restBore, _rig.InverseTransformDirection(_cradle.up));
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(_restBore, Vector3.up);
            if (side.sqrMagnitude < 1e-8f) side = _rig.InverseTransformDirection(_cradle.right);
            _axis = side.normalized;

            _ready = true;
            return true;
        }

        /// <summary>把 _phase 推向 _phaseTarget，落后越多追得越快。</summary>
        private void DriveFeed()
        {
            if (_phase < _phaseTarget)
            {
                // 连发时目标相位涨得和推进速度一样快，光靠等速推进会永远差半格。
                // 落后越多推得越快（最多 4 倍），追平后自动回到 550rpm 的正常节奏。
                float lag = _phaseTarget - _phase;
                float boost = 1f + Mathf.Clamp(lag, 0f, 3f);
                _phase = Mathf.MoveTowards(_phase, _phaseTarget, UnityEngine.Time.deltaTime / _stepSeconds * boost);
                if (_phase > _phaseTarget) _phase = _phaseTarget;
            }

            const float WrapAt = 1000f;
            if (_phaseTarget > WrapAt) { _phaseTarget -= WrapAt; _phase = Mathf.Max(0f, _phase - WrapAt); }
        }

        /// <summary>
        /// 弹链跟随枪机：位置绑 + 扭转 + 拉伸。
        ///
        /// 之前是"绕 GUN 原点转一个角度"，问题在于进弹口并不在 GUN 原点上 ——
        /// 一抬炮，进弹口是沿一段圆弧走的，绕原点转出来的弧和它对不上，
        /// 于是弹链头部就和进弹口错开了。
        ///
        /// 现在的做法是把弹链当作"一端焊在进弹口上、另一端钉在弹药箱上"的软东西：
        ///
        ///   1) 算 GUN 相对基准的刚体变换 dRot/dPos（BELT_RIG 空间）；
        ///   2) 每个链节的"完全跟随"位置 = 基准位置过一遍这个变换；
        ///   3) 按权重 w 在"完全跟随"和"原地不动"之间插值。
        ///
        /// w=1 的 LINK_01 严格跟着进弹口走（含圆弧、含后坐平移），
        /// w=0 的 LINK_34 钉死在弹药箱，中间的节被拉开 —— 拉伸就是这么来的。
        /// </summary>
        private void ApplyElevation()
        {
            Quaternion gunNowRot = Quaternion.Inverse(_rig.rotation) * _cradle.rotation;

            Quaternion dRot = gunNowRot * Quaternion.Inverse(_restGunRot);

            for (int k = 0; k < _links.Length; k++)
            {
                var b = _links[k];
                if (b == null) continue;

                float w = _weight[k];

                // 位置一律走世界坐标插值：
                //   静止端 = BELT_RIG 把这节的 rig 局部基准变换到当前帧（带镜像的父级也算得对）；
                //   跟随端 = 枪机把这节的枪机局部基准变换到当前帧（和进弹口同一个刚体，永远贴死）。
                // 之前把两端都塞进 rig 空间用四元数算，BELT_RIG 挂在带 (-1,1,-1) 镜像的
                // MOUNT VIS 下面，四元数表达不了负缩放的镜像，算出来的位置每抬一次炮就偏一个角度。
                Vector3 restWorld = _rig.TransformPoint(_restPos[k]);
                Vector3 rigidWorld = _cradle.TransformPoint(_restGunLocal[k]);
                b.position = Vector3.Lerp(restWorld, rigidWorld, w);

                Quaternion rigidRot = dRot * _restRot[k];
                Quaternion r = Quaternion.Slerp(_restRot[k], rigidRot, w);
                b.rotation = _rig.rotation * r;
            }
        }


        /// <summary>
        /// 子弹步进：每颗子弹在"自己这节"和"靠近枪机的下一节"之间按相位插值。
        ///
        /// 方向：LINK_01 在最靠近 M2 枪机的一端，编号越大越靠弹药箱，所以子弹是
        /// 从 LINK_k 往 LINK_{k-1} 走的；k=0 那颗按 0、1 两节的间距往枪机方向外推
        /// （它马上会被枪机吃掉）。
        ///
        /// 相位走满一格时它正好落在下一节的位置，而那一节那颗同时也回到本节，
        /// 两者互换，所以循环接缝看不出来。
        /// </summary>
        private void StepBullets()
        {
            if (_adv == null) return;

            float t = Mathf.Repeat(_phase, 1f);
            int n = _links.Length;

            for (int k = 0; k < n; k++)
            {
                var a = _adv[k];
                if (a == null) continue;

                Vector3 here = _links[k].position;
                Vector3 next;
                Quaternion rotNext;
                if (k > 0)
                {
                    next = _links[k - 1].position;
                    rotNext = _links[k - 1].rotation;
                }
                else
                {
                    // 已到枪机端：沿 0→1 的方向继续往枪机外侧推一节
                    next = here + (here - _links[1].position);
                    rotNext = _links[0].rotation;
                }

                a.position = Vector3.Lerp(here, next, t);
                a.rotation = Quaternion.Slerp(_links[k].rotation, rotNext, t);
            }
        }

        private Vector3 RigBore()
        {
            var bore = WorldBore();
            return bore == Vector3.zero ? _restBore : _rig.InverseTransformDirection(bore).normalized;
        }

        private Vector3 WorldBore()
        {
            if (_muzzle != null && _cradle != null)
            {
                var d = _muzzle.position - _cradle.position;
                if (d.sqrMagnitude > 1e-6f) return d.normalized;
            }
            return _cradle != null ? -_cradle.up : Vector3.zero;
        }


        private static void Warn(string m) => MelonLogger.Warning("[BELT] " + m);
    }
}
