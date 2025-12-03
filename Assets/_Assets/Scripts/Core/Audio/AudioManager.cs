using System.Collections;
using System.Collections.Generic;
using Hanzo.Core.Interfaces;
using Hanzo.Core.Utilities;
using Photon.Pun;
using UnityEngine;

namespace Hanzo.Audio
{
    [System.Serializable]
    public class AudioManager
    {
        [Header("Audio Settings")]
        public AudioClip audioClip;
        public AudioSource audioSource;

        [Tooltip("Distance at which sound starts to fade (full volume within this range)")]
        public float audioMinDistance = 5f;

        [Tooltip("Distance at which sound becomes inaudible")]
        public float audioMaxDistance = 25f;

        [Tooltip("How sound fades with distance (Logarithmic = realistic, Linear = gradual)")]
        public AudioRolloffMode audioRolloffMode = AudioRolloffMode.Logarithmic;
   
   
    }
}
