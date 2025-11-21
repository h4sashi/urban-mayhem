using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using System;

namespace DIndicator
{
    [CustomEditor(typeof(DirectionRegister))]
    public class DirectionRegisterEditor : Editor
    {
        private int amountDirectionIndicators = Enum.GetNames(typeof(DirectionIndicatorType)).Length;
        private DirectionRegister directionRegister;

        private void OnEnable()
        {
            directionRegister = (DirectionRegister)target;

            Array.Resize(ref directionRegister.directionIndicators, amountDirectionIndicators);
        }

        public override void OnInspectorGUI()
        {
            GUILayout.BeginVertical("Player Data", "window");
            GUILayout.BeginVertical("Box");
            base.OnInspectorGUI();
            GUILayout.EndVertical();
            GUILayout.EndVertical();

            GUILayout.Space(10);

            GUILayout.BeginVertical("Direction Indicators", "window");
            for (int i = 0; i < amountDirectionIndicators; i++)
            {
                GUILayout.BeginVertical("Box");

                EditorGUILayout.LabelField(Enum.GetName(typeof(DirectionIndicatorType), i) + " Indicator :");
                directionRegister.directionIndicators[i] = EditorGUILayout.ObjectField(directionRegister.directionIndicators[i], typeof(GameObject), false) as GameObject;

                if (directionRegister.directionIndicators[i] != null)
                {
                    if (directionRegister.directionIndicators[i].TryGetComponent(out DirectionIndicator directionIndicator))
                    {
                        directionIndicator.IndicatorType = (DirectionIndicatorType)i; 
                    }
                    else Debug.LogError("Cant find DirectionIndicator component in '" + directionRegister.directionIndicators[i].name + "' object");
                }

                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            SetDirtyDirectionRegister();
        }

        private void SetDirtyDirectionRegister()
        {
            if (GUI.changed)
            {
                EditorUtility.SetDirty(directionRegister);
                EditorSceneManager.MarkSceneDirty(directionRegister.gameObject.scene);
            }
        }
    }
}
