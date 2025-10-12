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
DressGirl
Puffer
Nutcracker
RedLocustBees
MouthDog
ForestGiant
RadMech
CaveDweller
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
#pragma warning disable CS8618
        public string enemyTypeName;

        public float reactionTime;

        public string movieTitle;

        public ScareVariant[] variants = [];
#pragma warning restore CS8618

        [System.Serializable]
        public struct ScareVariant
        {
            public string animStateName; // TODO: Find animation state names in lethal company unity project
            public AudioClip clip;
        }
    }
}
