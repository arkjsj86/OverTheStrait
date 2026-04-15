// UnityProject/Assets/Scripts/Data/ShipStatsSO.cs
using UnityEngine;

namespace HormuzAI.Data
{
    [CreateAssetMenu(fileName = "ShipStats", menuName = "HormuzAI/Ship Stats")]
    public class ShipStatsSO : ScriptableObject
    {
        [Header("Base Stats")]
        [Min(0.01f)]
        public float maxSpeed  = 10f;
        [Min(0.01f)]
        public float turnRate  = 1f;
        [Min(0.01f)]
        public float maxHealth = 100f;

        [Header("Depth Multipliers")]
        [Range(0f, 2f)]
        public float shallowSpeedMult = 0.7f;   // depthRatio < 0.33
        [Range(0f, 2f)]
        public float deepSpeedMult    = 1.2f;   // depthRatio > 0.67

        [Header("Width Multipliers")]
        [Range(0f, 2f)]
        public float narrowTurnMult = 1.3f;     // widthRatio < 0.5
        [Range(0f, 2f)]
        public float wideTurnMult   = 1.0f;     // widthRatio >= 0.5 (넓은 수로, 기본값)

        [Header("Health Multipliers")]
        [Range(0f, 2f)]
        public float damagedSpeedMult  = 0.8f;  // healthRatio <= 0.5
        [Range(0f, 2f)]
        public float criticalSpeedMult = 0.5f;  // healthRatio <= 0.2

        /// <summary>정규화된 수심 비율(0=얕음, 1=깊음)로 속도 multiplier 반환.</summary>
        public float GetDepthMult(float depthRatio)
        {
            depthRatio = Mathf.Clamp01(depthRatio);
            if (depthRatio < 0.33f) return shallowSpeedMult;
            if (depthRatio > 0.67f) return deepSpeedMult;
            return 1.0f;
        }

        /// <summary>정규화된 수로폭 비율(0=좁음, 1=넓음)로 선회율 multiplier 반환.</summary>
        public float GetWidthMult(float widthRatio)
        {
            widthRatio = Mathf.Clamp01(widthRatio);
            return widthRatio < 0.5f ? narrowTurnMult : wideTurnMult;
        }

        /// <summary>체력 비율(0=사망, 1=만땅)로 속도 multiplier 반환.</summary>
        public float GetHealthMult(float healthRatio)
        {
            healthRatio = Mathf.Clamp01(healthRatio);
            if (healthRatio <= 0.2f) return criticalSpeedMult;
            if (healthRatio <= 0.5f) return damagedSpeedMult;
            return 1.0f;
        }

        /// <summary>실효 속도 = maxSpeed × 수심 mult × 체력 mult.</summary>
        public float GetEffectiveSpeed(float depthRatio, float healthRatio)
            => maxSpeed * GetDepthMult(depthRatio) * GetHealthMult(healthRatio);

        /// <summary>실효 선회율 = turnRate × 수로폭 mult.</summary>
        public float GetEffectiveTurnRate(float widthRatio)
            => turnRate * GetWidthMult(widthRatio);
    }
}
