using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static SCP2006.Plugin;
using static SCP2006.Utils;

/* Animations:
- speed (float)
- sneaking (bool)
- scare
- laugh
- resting (bool)
- handOut (bool)
- think
- wave
- sh
 */

namespace SCP2006
{
    internal class SCP2006AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

        public static SCP2006AI? Instance { get; private set; }

#pragma warning disable CS8618
        public ParticleSystem particleSystem;
        public ScareDef[] scareDefs;
        public Transform turnCompass;
        //public GameObject meshObj;
        public AudioClip[] baitSFX;
        public Transform handTransform;
        public InteractTrigger handInteractTrigger;
#pragma warning restore CS8618

        HashSet<string> triggeredActions = new HashSet<string>();
        int score = 0;

        // point values per action
        Dictionary<string, int> points = new Dictionary<string, int>()
        {
            { "Yell", 4 },
            { "CameraTurn", 1 },
            { "Jump", 1 },
            { "Run", 3 },
            { "Attack", 2 },
            { "CloseDoor", 2 },
            { "NoLineOfSight", 3 }
        };

        public static Dictionary<string, int> learnedScares = [];

        Vector3 mainEntranceOutsidePosition;
        Vector3 mainEntranceInsidePosition;

        PlayerControllerB? lastSeenPlayer;

        VHSTapeBehavior? heldTape;

        bool facePlayer;
        float turnCompassSpeed = 30f;

        public bool isInsideFactory => !isOutside;

        float timeSinceStartReaction;
        float timeSinceLearnScare;
        float timeSinceStartScare;
        float timeSinceStartRoaming;
        float timeSinceLastSeen;

        int hashSneaking;

        EnemyAI? mimicEnemy;

        ScareDef? currentScareDef;
        int currentVariantIndex;
        bool usingDefaultScare;
        bool inScareAnimation;
        bool spottedByOtherPlayer;

        int currentFootstepSurfaceIndex;
        int previousFootstepClip;
        Transform? farthestNodeFromTargetPlayer;
        bool gettingFarthestNodeFromPlayerAsync;
        
        Vector2 lastCameraAngles;

        ScareDef.ScareVariant currentVariant => currentScareDef!.variants[currentVariantIndex];

        public enum ReactionType { Yell, Sprint, FastTurn, Jump}

        // Configs
        const float learnScareCooldown = 5f;
        const float targetPlayerCooldown = 10f;
        const float timeBetweenAnimationSteps = 0.1f;
        const float distanceToStartScare = 5f;
        const float distanceToStopScare = 10f;
        const float spottedLOSCooldown = 15f;
        const float timeToStopScare = 15f;
        const float lineOfSightOffset = 2f;
        const float playerScreamMinVolume = 0.9f;
        const float reactionDelay = 3f;
        const int scareSuccessfulScore = 5;
        const float maxTurnSpeed = 1000f;

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
            logger.LogDebug("SCP-2006 Spawned");
            mainEntranceInsidePosition = RoundManager.FindMainEntrancePosition();
            mainEntranceOutsidePosition = RoundManager.FindMainEntrancePosition(false, true);

            hashSneaking = Animator.StringToHash("sneaking");
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
            timeSinceStartReaction += Time.deltaTime;
            timeSinceStartScare += Time.deltaTime;
            timeSinceStartRoaming += Time.deltaTime;
            timeSinceLastSeen += Time.deltaTime;

            CustomEnemyAIUpdate();

            if (IsServer && gettingFarthestNodeFromPlayerAsync && lastSeenPlayer != null)
            {
                Transform transform = ChooseFarthestNodeFromPosition(maxAsyncIterations: 10, pos: lastSeenPlayer.transform.position, avoidLineOfSight: true, offset: 0, doAsync: true, capDistance: true);
                if (!gotFarthestNodeAsync)
                {
                    return;
                }
                farthestNodeFromTargetPlayer = transform;
                gettingFarthestNodeFromPlayerAsync = false;
            }
        }

        public void LateUpdate()
        {
            if (mimicEnemy != null)
            {
                mimicEnemy.transform.position = transform.position;
                mimicEnemy.transform.rotation = transform.rotation;
                //mimicEnemy.movingTowardsTargetPlayer = false;
                //mimicEnemy.SetDestinationToPosition(transform.position);
            }

            if (facePlayer && targetPlayer != null && IsServer)
            {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), turnCompassSpeed * Time.deltaTime);
            }

            creatureAnimator.SetBool(hashSneaking, currentBehaviourStateIndex == (int)State.Sneaking);
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
                    agent.speed = 5;
                    agent.stoppingDistance = 0;
                    facePlayer = false;

                    // Check line of sight for player
                    if (timeSinceStartRoaming > targetPlayerCooldown && TargetClosestPlayer(bufferDistance: default, requireLineOfSight: true))
                    {
                        if (InLineOfSightWithPlayer() && lastSeenPlayer != null && CheckLineOfSightForPosition(lastSeenPlayer.transform.position))
                        {
                            inSpecialAnimation = true;
                            DoAnimationClientRpc("wave");
                            targetPlayer = null;
                            targetNode = ChooseFarthestNodeFromPosition(transform.position, true);
                            SwitchToBehaviourClientRpc((int)State.Spotted);
                            return;
                        }

                        //agent.ResetPath();
                        currentScareDef = GetWeightedRandomScare();
                        currentVariantIndex = UnityEngine.Random.Range(0, currentScareDef.variants.Length);
                        //DoAnimationClientRpc("sneaking", true);
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
                    if (Utils.isBeta && Utils.DEBUG_disableMoving) { return; }
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
                    agent.speed = 7;
                    agent.stoppingDistance = distanceToStartScare;
                    facePlayer = false;

                    if (InLineOfSightWithPlayer()) // TODO: TEST THIS // TODO: Add finger shhh animation if not targetPlayer
                    {
                        inSpecialAnimation = true;
                        DoAnimationClientRpc("spotted");
                        //DoAnimationClientRpc("sneaking", false);
                        targetNode = ChooseFarthestNodeFromPosition(transform.position, true);
                        targetPlayer = null;
                        SwitchToBehaviourClientRpc((int)State.Spotted);
                        return;
                    }

                    if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= distanceToStartScare)
                    {
                        SpawnMimicEnemy();
                        //DoAnimationClientRpc("sneaking", false);
                        timeSinceStartScare = 0f;
                        spottedByOtherPlayer = false;
                        SwitchToBehaviourClientRpc((int)State.Scaring);
                        return;
                    }

                    if (Utils.isBeta && Utils.DEBUG_disableMoving) { return; }
                    if (!SetDestinationToPosition(targetPlayer.transform.position, checkForPath: true))
                    {
                        timeSinceStartRoaming = 0f;
                        targetPlayer = null;
                        //DoAnimationClientRpc("sneaking", false);
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }

                    break;

                case (int)State.Spotted:
                    agent.speed = 7;
                    agent.stoppingDistance = 0;

                    if (!InLineOfSightWithPlayer() && timeSinceLastSeen > spottedLOSCooldown)
                    {
                        timeSinceStartRoaming = 0f;
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                    }

                    // TODO: Check if player has tape here and 

                    if (Utils.isBeta && Utils.DEBUG_disableMoving) { return; }
                    AvoidPlayers();

                    break;

                case (int)State.Scaring:
                    agent.speed = 5;
                    agent.stoppingDistance = distanceToStartScare * 1.5f;
                    facePlayer = true;

                    if (targetPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * lineOfSightOffset)) // TODO: Add finger shhh animation if not targetPlayer
                    {
                        timeSinceLastSeen = 0f;
                        timeSinceStartReaction = 0f;
                        triggeredActions.Clear();
                        score = 0;
                        SwitchToBehaviourStateOnLocalClient((int)State.Reaction);
                        ScareClientRpc(); // Calls SwitchToBehaviourStateOnLocalClient((int)State.Reaction)
                        return;
                    }

                    if (!SetDestinationToPosition(targetPlayer.transform.position, true))
                    {
                        //RemoveScarePoint(currentScareDef!.enemyTypeName);
                        DespawnMimicEnemy();
                        timeSinceStartRoaming = 0f;
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }

                    if (Vector3.Distance(transform.position, targetPlayer.transform.position) > distanceToStopScare || timeSinceStartScare > timeToStopScare)
                    {
                        DespawnMimicEnemy();
                        SwitchToBehaviourClientRpc((int)State.Sneaking);
                        return;
                    }

                    if (!spottedByOtherPlayer && InLineOfSightWithPlayer())
                    {
                        spottedByOtherPlayer = true;
                        DoAnimationClientRpc("shush");
                    }

                    break;

                case (int)State.Reaction:
                    agent.speed = 0;
                    agent.stoppingDistance = 0;

                    facePlayer = true;

                    if (timeSinceStartReaction > reactionDelay)
                    {
                        if (targetPlayer.isSprinting) { TriggerReaction("Run"); }
                        if (targetPlayer.isJumping) { TriggerReaction("Jump"); }
                        TrackCameraMovement();
                        if (!triggeredActions.Contains("NoLineOfSight") && !HasLineOfSightToPlayer(targetPlayer)) { TriggerReaction("NoLineOfSight"); }
                    }

                    if ((timeSinceStartReaction - reactionDelay) > currentVariant.time)
                    {
                        // Count up points
                        if (score >= scareSuccessfulScore)
                        {
                            AddScarePoint(currentScareDef!.enemyTypeName);
                            inSpecialAnimation = true;
                            DoAnimationClientRpc("laugh");
                        }
                        else
                        {
                            RemoveScarePoint(currentScareDef!.enemyTypeName);
                        }

                        SwitchToBehaviourClientRpc((int)State.Spotted);
                        return;
                    }

                    break;

                case (int)State.Resting:
                    agent.speed = 0;
                    agent.stoppingDistance = 0;
                    facePlayer = false;

                    // TODO

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void TriggerReaction(string actionName)
        {
            // ignore if already triggered once
            if (triggeredActions.Contains(actionName)) return;

            triggeredActions.Add(actionName);

            if (points.TryGetValue(actionName, out int p))
                score += p;

            Debug.Log($"Triggered {actionName}: +{points[actionName]} (total {score})");
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

        ScareDef GetWeightedRandomScare()
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

        bool InLineOfSightWithPlayer(float width = 45f, int range = 20, int proximityAwareness = -1)
        {
            if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Clamp(range, 0, 30);
            }
            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                Vector3 position = player.gameplayCamera.transform.position;
                if (Vector3.Distance(position, eye.position) < (float)range && !Physics.Linecast(eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    Vector3 to = position - eye.position;
                    if (Vector3.Angle(eye.forward, to) < width || (proximityAwareness != -1 && Vector3.Distance(eye.position, position) < (float)proximityAwareness))
                    {
                        if (!player.HasLineOfSightToPosition(transform.position + Vector3.up * lineOfSightOffset, range: range)) { continue; }
                        timeSinceLastSeen = 0f;
                        lastSeenPlayer = player;
                        return true;
                    }
                }
            }
            return false;
        }

        bool HasLineOfSightToPlayer(PlayerControllerB player, float width = 45f, int range = 20, int proximityAwareness = -1)
        {
            if (isOutside && !enemyType.canSeeThroughFog && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Foggy)
            {
                range = Mathf.Clamp(range, 0, 30);
            }
            Vector3 position = player.gameplayCamera.transform.position;
            if (Vector3.Distance(position, eye.position) < (float)range && !Physics.Linecast(eye.position, position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                Vector3 to = position - eye.position;
                if (Vector3.Angle(eye.forward, to) < width || (proximityAwareness != -1 && Vector3.Distance(eye.position, position) < (float)proximityAwareness))
                {
                    lastSeenPlayer = player;
                    return true;
                }
            }
            return false;
        }

        void AvoidPlayers() // TODO: Test
        {
            if (farthestNodeFromTargetPlayer == null)
            {
                gettingFarthestNodeFromPlayerAsync = true;
                return;
            }
            Transform transform = farthestNodeFromTargetPlayer;
            farthestNodeFromTargetPlayer = null;
            if (transform != null)
            {
                facePlayer = false;
                targetNode = transform;
                SetDestinationToPosition(targetNode.position);
                return;
            }

            agent.speed = 0f;
            targetPlayer = lastSeenPlayer;
            facePlayer = true;
        }

        /* NoiseIDs
         * 6: player footsteps
         * 75: player voice chat
         */

        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
            logger.LogDebug($"Detected noise with id of: {noiseID} noiseLoudness: {noiseLoudness} timesPlayedInOneSpot: {timesPlayedInOneSpot}");
            // TODO
            if (timeSinceLearnScare > learnScareCooldown && currentBehaviourStateIndex == (int)State.Roaming)
            {
                Transform? closestNode = Utils.GetClosestAINodeToPosition(noisePosition);
                if (closestNode != null) { targetNode = closestNode; }
            }
            
            if (currentBehaviourStateIndex == (int)State.Reaction
                && targetPlayer != null
                && noiseLoudness >= playerScreamMinVolume
                && noiseID == 75
                && Vector3.Distance(targetPlayer.transform.position, noisePosition) < 1f)
            {
                TriggerReaction("Yell");
            }
        }

        void TrackCameraMovement()
        {
            if (triggeredActions.Contains("CameraTurn")) { return; }
            Vector2 currentAngles = new Vector2(targetPlayer.gameplayCamera.transform.eulerAngles.x, targetPlayer.gameplayCamera.transform.eulerAngles.y);

            // Calculate delta, account for angle wrapping (360 to 0)
            float deltaX = Mathf.DeltaAngle(lastCameraAngles.x, currentAngles.x);
            float deltaY = Mathf.DeltaAngle(lastCameraAngles.y, currentAngles.y);

            // Combine both axes into a single turn speed value
            float cameraTurnSpeed = new Vector2(deltaX, deltaY).magnitude / Time.deltaTime;
            lastCameraAngles = currentAngles;

            if (cameraTurnSpeed > maxTurnSpeed)
            {
                TriggerReaction("CameraTurn");
            }
        }

        public bool PlayerIsTargetable(PlayerControllerB playerScript)
        {
            if (playerScript.isPlayerControlled && !playerScript.isPlayerDead && playerScript.inAnimationWithEnemy == null && playerScript.sinkingValue < 0.73f)
            {
                return true;
            }
            return false;
        }

        public void SpawnMimicEnemy()
        {
            if (currentScareDef == null || currentScareDef.enemyTypeName == "") { return; }
            Enemies.SpawnableEnemy spawnableEnemy = Enemies.spawnableEnemies.Where(x => x.enemy.name == currentScareDef!.enemyTypeName).First();
            GameObject enemyPrefab = spawnableEnemy.enemy.enemyPrefab;
            GameObject enemyObj = Instantiate(enemyPrefab, transform.position, transform.rotation, transform);
            enemyObj.GetComponent<NetworkObject>().Spawn(true);
            mimicEnemy = enemyObj.GetComponent<EnemyAI>();
            mimicEnemy.enabled = false;

            SpawnMimicEnemyClientRpc(mimicEnemy.NetworkObject, currentVariantIndex);
        }

        public void DespawnMimicEnemy()
        {
            if (mimicEnemy == null || !mimicEnemy.NetworkObject.IsSpawned) { return; }
            logger.LogDebug("Despawning mimic enemy " + mimicEnemy.enemyType.name);

            mimicEnemy.NetworkObject.Despawn(true);
            mimicEnemy = null;
            EnableEnemyMesh(true);
            DespawnMimicEnemyClientRpc();
        }

        void GetCurrentMaterialStandingOn()
        {
            Ray interactRay = new Ray(transform.position + Vector3.up, -Vector3.up);
            if (!Physics.Raycast(interactRay, out RaycastHit hit, 6f, StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore) || hit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].surfaceTag))
            {
                return;
            }
            for (int i = 0; i < StartOfRound.Instance.footstepSurfaces.Length; i++)
            {
                if (hit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[i].surfaceTag))
                {
                    currentFootstepSurfaceIndex = i;
                    break;
                }
            }
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1) // Synced
        {
            logger.LogDebug("In HitEnemy()");

            if (!IsServer) { return; }

            if (currentBehaviourStateIndex == (int)State.Reaction)
            {
                TriggerReaction("Attack");
                return;
            }

            inSpecialAnimation = true;
            DoAnimationClientRpc("spotted");
            //DoAnimationClientRpc("sneaking", false);
            targetNode = ChooseFarthestNodeFromPosition(transform.position, true);
            targetPlayer = null;
            SwitchToBehaviourClientRpc((int)State.Spotted);
        }

        // Animation Functions

        public void SetInSpecialAnimationFalse() => inSpecialAnimation = false;
        public void SetInSpecialAnimationTrue() => inSpecialAnimation = true;

        public void PlayFootstepSFX()
        {
            if (currentBehaviourStateIndex == (int)State.Sneaking) { return; }
            GetCurrentMaterialStandingOn();
            int index = Random.Range(0, StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length);
            if (index == previousFootstepClip)
            {
                index = (index + 1) % StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length;
            }
            creatureSFX.pitch = Random.Range(0.93f, 1.07f);
            creatureSFX.PlayOneShot(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips[index], 0.6f);
            previousFootstepClip = index;
        }

        public void GiveTape() // InteractTrigger
        {
            logger.LogDebug("Giving tape to SCP-2006");
            VHSTapeBehavior? tape = localPlayer.currentlyHeldObjectServer as VHSTapeBehavior;
            if (tape == null) { return; }

            localPlayer.DiscardHeldObject();
            GiveTapeServerRpc(tape.NetworkObject);
        }

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void GiveTapeServerRpc(NetworkObjectReference netRef)
        {
            if (!IsServer) { return; }
            GiveTapeClientRpc(netRef);
        }

        [ClientRpc]
        public void GiveTapeClientRpc(NetworkObjectReference netRef)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { return; }
            if (!netObj.TryGetComponent(out VHSTapeBehavior tape)) { return; }

            creatureAnimator.SetBool("handOut", false);

            tape.parentObject = handTransform;
            tape.hasHitGround = false;
            tape.isHeldByEnemy = true;
            tape.GrabItemFromEnemy(this);
            tape.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(tape.gameObject);
            heldTape = tape;
        }

        [ClientRpc]
        public void DropTapeClientRpc(Vector3 position)
        {
            if (heldTape == null)
            {
                return;
            }
            GrabbableObject tape = heldTape;
            tape.parentObject = null;
            tape.transform.SetParent(StartOfRound.Instance.propsContainer, worldPositionStays: true);
            tape.EnablePhysics(enable: true);
            tape.fallTime = 0f;
            tape.startFallingPosition = tape.transform.parent.InverseTransformPoint(tape.transform.position);
            tape.targetFloorPosition = tape.transform.parent.InverseTransformPoint(position);
            tape.floorYRot = -1;
            tape.DiscardItemFromEnemy();
            tape.isHeldByEnemy = false;
            HoarderBugAI.grabbableObjectsInMap.Add(tape.gameObject);
            heldTape = null;
        }

        [ClientRpc]
        public void DespawnMimicEnemyClientRpc()
        {
            mimicEnemy = null;
            particleSystem.Play();
            EnableEnemyMesh(true);
        }

        [ClientRpc]
        public void SpawnMimicEnemyClientRpc(NetworkObjectReference netRef, int variantIndex)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt find network object in SpawnMimicEnemyClientRpc"); return; }
            if (!netObj.TryGetComponent(out mimicEnemy)) { logger.LogError("Couldnt find EnemyAI component in SpawnMimicEnemyClientRpc"); return; }
            if (mimicEnemy == null) { return; }

            /*foreach (var collider in mimicEnemy.transform.root.gameObject.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }*/
            //mimicEnemy.inSpecialAnimation = true; // TODO: Test this

            mimicEnemy.enabled = false; // TODO: Test this

            particleSystem.Play();
            EnableEnemyMesh(false);

            currentScareDef = scareDefs.Where(x => x.enemyTypeName == mimicEnemy.enemyType.name).First();
            currentVariantIndex = variantIndex;

            RoundManager.PlayRandomClip(creatureSFX, baitSFX);
        }

        [ClientRpc]
        public void ScareClientRpc()
        {
            creatureVoice.Stop();
            creatureVoice.clip = currentVariant.clip;
            creatureVoice.Play();

            mimicEnemy?.creatureAnimator.Play(currentVariant.animStateName); // TODO: Test this
            SwitchToBehaviourStateOnLocalClient((int)State.Reaction);
        }

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

    [HarmonyPatch]
    internal class SCP2006AIPatches
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DoorLock), nameof(DoorLock.OpenOrCloseDoor))]
        public static void OpenOrCloseDoorPostfix(DoorLock __instance, PlayerControllerB playerWhoTriggered)
        {
            try
            {
                if (!IsServerOrHost
                    || SCP2006AI.Instance == null
                    || SCP2006AI.Instance.currentBehaviourStateIndex != (int)SCP2006AI.State.Reaction
                    || playerWhoTriggered != SCP2006AI.Instance.targetPlayer)
                { return; }

                SCP2006AI.Instance.TriggerReaction("CloseDoor");
            }
            catch
            {
                return;
            }
        }
    }
}