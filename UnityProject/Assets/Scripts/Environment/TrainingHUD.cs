// UnityProject/Assets/Scripts/Environment/TrainingHUD.cs
using UnityEngine;
using HormuzAI.Data;

namespace HormuzAI.Environment
{
    public class TrainingHUD : MonoBehaviour
    {
        [SerializeField] ShipStatsSO shipStats;

        static readonly GUIStyle _style = new GUIStyle();
        bool _styleInit;

        void OnGUI()
        {
            if (!_styleInit)
            {
                _style.fontSize  = 18;
                _style.fontStyle = FontStyle.Bold;
                _style.normal.textColor = Color.white;
                _style.alignment = TextAnchor.UpperRight;
                _styleInit = true;
            }

            float speed = shipStats != null ? shipStats.maxSpeed : 0f;
            string text = $"TimeScale : {Time.timeScale:F0}\nShip Speed : {speed:F0}";

            float w = 200f, h = 60f;
            float x = Screen.width - w - 10f;
            GUI.Label(new Rect(x, 10f, w, h), text, _style);
        }
    }
}
