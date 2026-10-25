using UnityEngine;

namespace ModUtil
{
    /// <summary>
    /// Transform 查找工具。prefab 层级随模型重导出变过几次，按名字的深度优先查找比
    /// Transform.Find 的固定路径可靠，所以 CROWS.Mount 和 CROWS.Belt 共用这一份。
    /// </summary>
    internal static class Transforms
    {
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
