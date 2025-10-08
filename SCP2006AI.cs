using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP2006.Plugin;

namespace SCP2006
{
    internal class SCP2006AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

        public static SCP2006AI? Instance { get; private set; }

#pragma warning disable CS8618
        public Transform turnCompass;
#pragma warning restore CS8618

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntranceInsidePosition;

        public bool isInsideFactory => !isOutside;

        float timeSincePlayerCollision;

        public enum State
        {
            Roaming,
            Sneaking,
            Spotted,
            Scaring,
            Reaction,
            Resting
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-4666 Spawned");

            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-2006 in the scene. Removing this one.");
                if (!IsServer) { return; }
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-2006");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void CustomEnemyAIUpdate()
        {
            if (inSpecialAnimation)
            {
                agent.speed = 0f;
                return;
            }

            if (updateDestinationInterval >= 0f)
            {
                updateDestinationInterval -= Time.deltaTime;
            }
            else
            {
                DoAIInterval();
                updateDestinationInterval = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
            }
        }

        public override void Update()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            CustomEnemyAIUpdate();
        }

        public override void DoAIInterval()
        {
            if (moveTowardsDestination)
            {
                agent.SetDestination(destination);
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:

                    break;

                case (int)State.Sneaking:

                    break;

                case (int)State.Spotted:

                    break;

                case (int)State.Scaring:

                    break;

                case (int)State.Reaction:

                    break;

                case (int)State.Resting:

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void Teleport(Vector3 position, bool outside)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            transform.position = position;
            SetEnemyOutsideClientRpc(outside);
        }

        void SetDestinationToEntrance()
        {
            if (agent == null || agent.enabled == false) { return; }
            if (isInsideFactory)
            {
                SetDestinationToPosition(mainEntranceInsidePosition);

                if (Vector3.Distance(transform.position, mainEntranceInsidePosition) < 1f)
                {
                    Teleport(mainEntranceOutsidePosition, true);
                }
            }
            else
            {
                SetDestinationToPosition(mainEntranceOutsidePosition);

                if (Vector3.Distance(transform.position, mainEntranceOutsidePosition) < 1f)
                {
                    Teleport(mainEntranceInsidePosition, false);
                }
            }
        }

        bool InLineOfSight()
        {
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!PlayerIsTargetable(player)) { continue; }
                if (player.HasLineOfSightToPosition(transform.position + Vector3.up * 2)) { return true; }
            }

            return false;
        }

        public bool PlayerIsTargetable(PlayerControllerB playerScript)
        {
            if (playerScript.isPlayerControlled && !playerScript.isPlayerDead && playerScript.inAnimationWithEnemy == null && playerScript.sinkingValue < 0.73f)
            {
                return true;
            }
            return false;
        }

        #region Overrides

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1) // Synced
        {
            logger.LogDebug("In HitEnemy()");


        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead) { return; }
            //if (timeSincePlayerCollision < 3f) { return; }
            if (inSpecialAnimation) { return; }
            PlayerControllerB? player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !PlayerIsTargetable(player) || player != localPlayer) { return; }


        }


        #endregion

        #region Animation
        // Animation Functions



        #endregion

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            if (!IsServer) { return; }
            DoAnimationClientRpc(animationName);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        public void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }
    }
}