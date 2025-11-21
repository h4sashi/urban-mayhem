using UnityEngine;

using System;
using System.Collections.Generic;

using DIndicator.Utils;

namespace DIndicator
{
    /// <summary>
    /// Class for indicators operation
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class DirectionRegister : MonoBehaviour
    {
        public static DirectionRegister Instance { get; private set; }

        //The camera that is or is watching the player
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private Transform _player;
        [HideInInspector] public GameObject[] directionIndicators;

        private MultiMap<Transform, DirectionIndicator> createdIndicators = new MultiMap<Transform, DirectionIndicator>();

        #region MONO

        private void Awake()
        {
            this.name = typeof(DirectionRegister).Name;

            if (Instance == null)
            {
                DontDestroyOnLoad(Instance = this);
            }
            else Destroy(this.gameObject);
        }

        #endregion

        /// <summary>
        /// Creates an indicator for showing a target
        /// </summary>
        /// <param name="target">Target to be tracked</param>
        /// <param name="indicatorType">Type of indicator to be created</param>
        /// <returns></returns>
        public DirectionIndicator CreateDirectionIndicator(Transform target, DirectionIndicatorType indicatorType)
        {
            DirectionIndicator directionIndicator = null;

            if (TryGetDirectionIndicator(out GameObject directionIndicatorObj, indicatorType))
            {
                if (TryFindDirectionIndicator(target, indicatorType, out directionIndicator)) return directionIndicator;

                GameObject spawnObj = Instantiate(directionIndicatorObj);
                if (spawnObj.TryGetComponent(out directionIndicator))
                {
                    spawnObj.transform.SetParent(this.transform, false);
                    directionIndicator.InitIndicator(target, _player, _playerCamera);
                    createdIndicators.Add(target, directionIndicator);
                    directionIndicator.DestroyDirectionIndicator += (() => createdIndicators.Remove(target, directionIndicator));
                }
            }

            return directionIndicator;
        }

        /// <summary>
        /// Delete all created indicators
        /// </summary>
        public void ClearIndicators()
        {
            createdIndicators.Clear();

            foreach (Transform child in this.transform)
            {
                Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// Tries to find the created indicator
        /// </summary>
        private bool TryFindDirectionIndicator(Transform target, DirectionIndicatorType indicatorType, out DirectionIndicator directionIndicator)
        {
            bool foundIndicator = false;
            directionIndicator = null;

            if (createdIndicators.TryGetValue(target, out List<DirectionIndicator> indicators))
            {
                foreach (var indicator in indicators)
                {
                    if (indicator.IndicatorType.Equals(indicatorType))
                    {
                        directionIndicator = indicator;
                        foundIndicator = true;

                        indicator.ShowIndicator();
                    }
                }
            }

            return foundIndicator;
        }

        //Tries to find in the array with indicator prefabs
        private bool TryGetDirectionIndicator(out GameObject directionIndicator, DirectionIndicatorType indicatorType)
        {
            directionIndicator = directionIndicators[(int)indicatorType];
            bool isGetDirectionIndicator = !(directionIndicator == null);

            if (!isGetDirectionIndicator)
            {
                Debug.LogError("Cant find DirectionIndicator '" + Enum.GetName(typeof(DirectionIndicatorType), indicatorType) + "' object in DirectionRegister");
            }

            return isGetDirectionIndicator;
        }
    }
}
