using System;
using GHPC;
using GHPC.Vehicle;
using UnityEngine;

namespace M1A1Abrams
{
    public partial class CrowsAutoTrack : MonoBehaviour
    {
        /// <summary>
        /// The platform's velocity as the chassis reports it, with no filter on it, for the lead; see
        /// PlatformLeadVelocity. The same sample also drives the platform acceleration estimate.
        /// </summary>
        private Vector3 platform_velocity_raw;
        private bool platform_velocity_raw_valid;
        private Vector3 platform_acceleration;
        private Vector3 platform_velocity_previous;
        private float platform_motion_time;
        private bool platform_motion_sample_valid;

        private void UpdatePlatformMotion()
        {
            // Target motion and relative UKF state use the physics clock too.
            // Update/LateUpdate callers must not differentiate one physics sample twice.
            float motion_now = Time.fixedTime;
            if (platform_motion_sample_valid && motion_now == platform_motion_time) return;
            Vector3 velocity = Vector3.zero;
            bool known = false;

            try
            {
                if (fcs != null && fcs.Chassis != null)
                {
                    velocity = fcs.Chassis.Velocity;
                    known = true;
                }
            }
            catch (Exception)
            {
                known = false;
            }

            if (!known && ours != null)
            {
                try
                {
                    velocity = ours?.Chassis?.Velocity ?? Vector3.zero;
                    known = true;
                }
                catch (Exception)
                {
                    // A vehicle that will not report a velocity simply reads as stopped.
                }
            }

            if (!known)
            {
                velocity = Vector3.zero;
            }

            // Keep the raw sample for the relative lead and derive acceleration from the same clock.
            platform_velocity_raw = velocity;
            platform_velocity_raw_valid = known;

            if (known && platform_motion_sample_valid)
            {
                float motion_dt = motion_now - platform_motion_time;
                if (motion_dt > 1e-4f && motion_dt <= 0.25f)
                {
                    Vector3 measured_acceleration = (velocity - platform_velocity_previous) / motion_dt;
                    platform_acceleration = Vector3.Lerp(platform_acceleration,
                        Vector3.ClampMagnitude(measured_acceleration, 12f),
                        1f - Mathf.Exp(-8f * motion_dt));
                }
            }
            else if (!known)
            {
                platform_acceleration = Vector3.zero;
            }

            platform_velocity_previous = velocity;
            platform_motion_time = motion_now;
            platform_motion_sample_valid = known;
        }
    }
}
