using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/*
[Debug  :   SCP2006] Centipede
[Debug  :   SCP2006] SandSpider
[Debug  :   SCP2006] HoarderBug
[Debug  :   SCP2006] Flowerman
[Debug  :   SCP2006] Crawler
[Debug  :   SCP2006] Blob
[Debug  :   SCP2006] DressGirl
[Debug  :   SCP2006] Puffer
[Debug  :   SCP2006] Nutcracker
[Debug  :   SCP2006] SCP4271Enemy
[Debug  :   SCP2006] SCP323_1Enemy
[Debug  :   SCP2006] RedLocustBees
[Debug  :   SCP2006] Doublewing
[Debug  :   SCP2006] DocileLocustBees
[Debug  :   SCP2006] MouthDog
[Debug  :   SCP2006] ForestGiant
[Debug  :   SCP2006] SandWorm
[Debug  :   SCP2006] RadMech
[Debug  :   SCP2006] SCP4666Enemy
[Debug  :   SCP2006] ClaySurgeon
[Debug  :   SCP2006] CaveDweller
[Debug  :   SCP2006] FlowerSnake
[Debug  :   SCP2006] GiantKiwi
[Debug  :   SCP2006] BaboonHawk
[Debug  :   SCP2006] SpringMan
[Debug  :   SCP2006] Jester
[Debug  :   SCP2006] LassoMan
[Debug  :   SCP2006] MaskedPlayerEnemy
[Debug  :   SCP2006] Butler
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
