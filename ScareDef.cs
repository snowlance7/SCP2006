using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/*
Centipede
SandSpider
HoarderBug
Flowerman
Crawler
// Blob
DressGirl
Puffer
Nutcracker
RedLocustBees
MouthDog
ForestGiant
//SandWorm
RadMech
ClaySurgeon
CaveDweller
FlowerSnake
GiantKiwi
BaboonHawk
SpringMan
Jester
MaskedPlayerEnemy
Butler
*/

namespace SCP2006
{
    [CreateAssetMenu(menuName = "SCP2006/ScareData")]
    public class ScareDef : ScriptableObject
    {
        public static ScareDef[] scareDefs = []; // TODO: Fill this in code or unity

#pragma warning disable CS8618
        public string enemyTypeName;
        public bool outside;

        public string movieTitle;
        public Sprite movieCover;

        public ScareVariant[] variants = [];

        public AudioClip[]? baitSFX;
#pragma warning restore CS8618

        [System.Serializable]
        public struct ScareVariant
        {
            public string animStateName; // TODO: Find animation state names in lethal company unity project
            public AudioClip clip;
            public float time;
        }
    }
}
