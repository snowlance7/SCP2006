using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace SCP2006
{
    internal class VHSTapeBehavior : PhysicsProp
    {
#pragma warning disable CS8618
        public ScareDef[] scareDefs;
        public SpriteRenderer spriteRenderer;
#pragma warning restore CS8618

        public static HashSet<VHSTapeBehavior> spawnedTapes = [];

        public int scareDefIndex;

        public override void Start()
        {
            base.Start();

            spawnedTapes.Add(this);

            if (!IsServer) { return; }
            int index = UnityEngine.Random.Range(0, scareDefs.Length);
        }

        public override void OnDestroy()
        {
            spawnedTapes.Remove(this);
            base.OnDestroy();
        }

        [ClientRpc]
        public void ChangeCoverClientRpc(int _scareDefIndex)
        {
            scareDefIndex = _scareDefIndex;
            spriteRenderer.sprite = scareDefs[scareDefIndex].movieCover;
        }
    }
}
