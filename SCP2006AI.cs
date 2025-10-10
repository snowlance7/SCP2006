using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.ProBuilder;
using static SCP2006.Plugin;
using static SCP2006.Utils;
using LethalLib.Modules;

/* Animations:
- speed (float)
- sneaking (bool)
- scare
- laugh
- resting (bool)
- handOut (bool)
- think
 */

namespace SCP2006
{
    internal class SCP2006AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

        public static SCP2006AI? Instance { get; private set; }

#pragma warning disable CS8618
        public ScareDef[] scareDefs;
        public Transform turnCompass;
        public GameObject mesh;
#pragma warning restore CS8618

        public static Dictionary<string, int> learnedScares = [];

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntranceInsidePosition;

        public bool isInsideFactory => !isOutside;

        float timeSincePlayerCollision;
        float timeSinceReaction;
        float timeSinceLearnScare;
        float timeSinceStartScare;
        float timeSinceTargetPlayer;

        EnemyAI? mimicEnemy;
        float currentScareAnimationTime;
        ScareDef? currentScareDef;
        int currentScareVariantIndex;

        // Configs
        const float learnScareCooldown = 5f;
        const float targetPlayerCooldown = 10f;

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

            timeSinceLearnScare += Time.deltaTime;
            timeSincePlayerCollision += Time.deltaTime;
            timeSinceReaction += Time.deltaTime;
            timeSinceStartScare += Time.deltaTime;
            timeSinceTargetPlayer += Time.deltaTime;

            CustomEnemyAIUpdate();
        }

        public void LateUpdate()
        {
            if (mimicEnemy != null)
            {
                mimicEnemy.movingTowardsTargetPlayer = false;
                mimicEnemy.SetDestinationToPosition(transform.position);
            }
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

                    // Check line of sight for player
                    if (timeSinceTargetPlayer > targetPlayerCooldown && TargetClosestPlayer(bufferDistance: default, requireLineOfSight: true))
                    {
                        timeSinceTargetPlayer = 0f;

                        if (InLineOfSight())
                        {
                            inSpecialAnimation = true;
                            DoAnimationClientRpc("shock");
                            SwitchToBehaviourClientRpc((int)State.Spotted); // TODO: Maybe switch to him waving hello?
                            return;
                        }

                        agent.ResetPath();
                        currentScareDef = GetRandomScare();
                        currentScareVariantIndex = UnityEngine.Random.Range(0, currentScareDef.variants.Length);
                        SwitchToBehaviourClientRpc((int)State.Sneaking);
                        return;
                    }

                    // Check for enemy to learn scare from in line of sight
                    if (timeSinceLearnScare > learnScareCooldown)
                    {
                        GameObject? seenEnemy = CheckLineOfSight(RoundManager.Instance.SpawnedEnemies    // TODO: Test this
                                .Where(e => scareDefs.Any(s => s.enemyTypeName == e.enemyType.name))
                                .Select(e => e.gameObject)
                                .ToList());

                        if (seenEnemy != null)
                        {
                            if (seenEnemy.TryGetComponent(out EnemyAI seenEnemyAI))
                            {
                                AddScarePoint(seenEnemyAI.enemyType.name);
                                timeSinceLearnScare = 0f;
                                inSpecialAnimation = true;
                                DoAnimationClientRpc("think");
                            }
                        }
                    }

                    // Roam logic
                    if (HasReachedTargetNode())
                    {
                        targetNode = Utils.GetRandomNode()?.transform;
                    }
                    if (targetNode != null && !SetDestinationToPosition(targetNode.position, checkForPath: true))
                    {
                        if (!SetDestinationToEntrance())
                        {
                            targetNode = null;
                        }
                    } // TODO: Test this

                    break;

                case (int)State.Sneaking:

                    if (InLineOfSight())
                    {
                        inSpecialAnimation = true;
                        DoAnimationClientRpc("shock");
                        SwitchToBehaviourClientRpc((int)State.Spotted);
                        return;
                    }

                    var currentVariant = currentScareDef!.variants[currentScareVariantIndex];
                    float distanceToScare = currentVariant.windUp ? currentVariant.distanceToWindup : currentVariant.distanceToScare;

                    if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= distanceToScare)
                    {
                        TrySpawnMimicEnemy();
                        SwitchToBehaviourClientRpc((int)State.Scaring); // TODO: Continue here
                        return;
                    }

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

        void AddScarePoint(string enemyTypeName)
        {
            if (!learnedScares.ContainsKey(enemyTypeName)) { learnedScares.Add(enemyTypeName, 0); }

            learnedScares[enemyTypeName]++;
        }

        void RemoveScarePoint(string enemyTypeName)
        {
            if (!learnedScares.ContainsKey(enemyTypeName) || learnedScares[enemyTypeName] <= 1) { return; }

            learnedScares[enemyTypeName]--;
        }

        ScareDef GetRandomScare()
        {
            string[] scareNames = learnedScares.Keys.ToArray();
            int[] weights = learnedScares.Values.ToArray();

            int randomIndex = RoundManager.Instance.GetRandomWeightedIndex(weights);
            return scareDefs.Where(x => x.enemyTypeName == scareNames[randomIndex]).First();
        }

        bool HasReachedDestination()
        {
            if (!agent.pathPending) // Wait until the path is calculated
            {
                if (agent.remainingDistance <= agent.stoppingDistance)
                {
                    if (!agent.hasPath || agent.velocity.sqrMagnitude == 0f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        bool HasReachedTargetNode()
        {
            if (targetNode == null) { return true; }
            return Vector3.Distance(transform.position, targetNode.position) <= 1f;
        }

        public void Teleport(Vector3 position, bool outside)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            transform.position = position;
            SetEnemyOutsideClientRpc(outside);
        }

        bool SetDestinationToEntrance()
        {
            if (agent == null || agent.enabled == false) { return false; }
            if (isInsideFactory)
            {
                if (!SetDestinationToPosition(mainEntranceInsidePosition, checkForPath: true)) { return false; }

                if (Vector3.Distance(transform.position, mainEntranceInsidePosition) < 1f)
                {
                    Teleport(mainEntranceOutsidePosition, true);
                }
            }
            else
            {
                if (!SetDestinationToPosition(mainEntranceOutsidePosition, checkForPath: true)) { return false; }

                if (Vector3.Distance(transform.position, mainEntranceOutsidePosition) < 1f)
                {
                    Teleport(mainEntranceInsidePosition, false);
                }
            }

            return true;
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

        public void TrySpawnMimicEnemy()
        {
            if (currentScareDef == null || currentScareDef.enemyTypeName == "") { return; }
            Enemies.SpawnableEnemy spawnableEnemy = Enemies.spawnableEnemies.Where(x => x.enemy.name == currentScareDef!.enemyTypeName).First();
            GameObject enemyPrefab = spawnableEnemy.enemy.enemyPrefab;
            GameObject enemyObj = Instantiate(enemyPrefab, transform.position, transform.rotation, transform);
            enemyObj.GetComponent<NetworkObject>().Spawn(true);
            mimicEnemy = enemyObj.GetComponent<EnemyAI>();

            SpawnMimicEnemyClientRpc(mimicEnemy.NetworkObject, currentScareVariantIndex);
        }

        public void DespawnMimicEnemy()
        {
            if (mimicEnemy == null || !mimicEnemy.NetworkObject.IsSpawned) { return; }
            logger.LogDebug("Despawning mimic enemy " + mimicEnemy.enemyType.name);

            switch (mimicEnemy.enemyType.name)
            {
                case "Butler":
                    ButlerEnemyAI.murderMusicAudio.Stop();
                    break;
                default:
                    break;
            }

            mimicEnemy.NetworkObject.Despawn(true);
            mimicEnemy = null;
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

        public void SetInSpecialAnimationFalse() => inSpecialAnimation = false;
        public void SetInSpecialAnimationTrue() => inSpecialAnimation = true;

        #endregion

        // RPC's

        [ClientRpc]
        public void SpawnMimicEnemyClientRpc(NetworkObjectReference netRef, int variantIndex)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt find network object in SpawnMimicEnemyClientRpc"); return; }
            if (!netObj.TryGetComponent(out mimicEnemy)) { logger.LogError("Couldnt find EnemyAI component in SpawnMimicEnemyClientRpc"); return; }

            foreach (var collider in mimicEnemy!.transform.root.gameObject.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }

            mimicEnemy.inSpecialAnimation = true; // TODO: Test this

            mesh.SetActive(false);

            ScareDef scareDef = scareDefs.Where(x => x.enemyTypeName == mimicEnemy!.enemyType.name).First();
            ScareDef.ScareVariant animationAudio = scareDef.variants[variantIndex];

            creatureVoice.clip = animationAudio.clip;
            creatureVoice.Play();
            mimicEnemy!.creatureAnimator.SetTrigger(animationAudio.animName);
        }

        /*[ClientRpc]
        public void ScareClientRpc(NetworkObjectReference netRef, int variantIndex)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt find network object in ScareClientRpc"); return; }
            if (!netObj.TryGetComponent(out mimicEnemy)) { logger.LogError("Couldnt find EnemyAI component in ScareClientRpc"); return; }

            mesh.SetActive(false);

            ScareDef scareDef = scareDefs.Where(x => x.enemyTypeName == mimicEnemy!.enemyType.name).First();
            ScareDef.AnimationAudio animationAudio = scareDef.variants[variantIndex];

            creatureVoice.clip = animationAudio.clip;
            creatureVoice.Play();
            mimicEnemy!.creatureAnimator.SetTrigger(animationAudio.animName);
        }*/

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName, bool value)
        {
            if (!IsServer) { return; }
            DoAnimationClientRpc(animationName, value);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName, bool value)
        {
            creatureAnimator.SetBool(animationName, value);
        }

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