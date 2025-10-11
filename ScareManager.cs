/*using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.ProBuilder.Csg;
using UnityEngine.UIElements;
using static SCP2006.Plugin;
using static UnityEngine.VFX.VisualEffectControlTrackController;

namespace SCP2006
{
    public class ScareManager : MonoBehaviour // Parented to SCP-2006 in unity
    {
        private static ManualLogSource logger = LoggerInstance;

        public static ScareManager? Instance { get; private set; }

        delegate void MethodDelegate();

        List<MethodDelegate> commonEvents = new List<MethodDelegate>();
        List<MethodDelegate> uncommonEvents = new List<MethodDelegate>();
        List<MethodDelegate> rareEvents = new List<MethodDelegate>();

        List<Action> cleanupActions = [];

        Coroutine? activeCoroutine;
        Action? activeCoroutineCleanup;
        int currentCoroutineTier = -1; // -1 = none, 0 = common, 1 = uncommon, 2 = rare
        bool wasInterrupted = false;

        public void Start()
        {
            // Common Events
            commonEvents.Add(FlickerLights);
            commonEvents.Add(PlayAmbientSFXNearby);
            commonEvents.Add(PlayFakeSoundEffectMinor);
            commonEvents.Add(PlayBellSFX);
            commonEvents.Add(HideHazard);
            commonEvents.Add(Stare);
            logger.LogDebug("CommonEvents: " + commonEvents.Count);

            // Uncommon Events
            uncommonEvents.Add(FarStare);
            uncommonEvents.Add(Jumpscare);
            uncommonEvents.Add(BlockDoor);
            uncommonEvents.Add(StalkPlayer);
            uncommonEvents.Add(PlayFakeSoundEffectMajor);
            uncommonEvents.Add(ShowFakeShipLeavingDisplayTip);
            uncommonEvents.Add(SpawnFakeBody);
            uncommonEvents.Add(SlowWalkToPlayer);
            logger.LogDebug("UncommonEvents: " + uncommonEvents.Count);

            // Rare Events
            rareEvents.Add(MimicEnemyChase);
            rareEvents.Add(MimicPlayer);
            rareEvents.Add(ChasePlayer);
            rareEvents.Add(SpawnGhostGirl);
            rareEvents.Add(TurnOffAllLights);
            rareEvents.Add(SpawnFakeLandminesAroundPlayer);
            rareEvents.Add(SpawnMultipleFakeBodies);
            rareEvents.Add(ForceSuicide);
            //rareEvents.Add(MimicJester);
            logger.LogDebug("RareEvents: " + rareEvents.Count);

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void OnDestroy()
        {
            StopAllCoroutines();

            foreach (var cleanup in cleanupActions)
                cleanup?.Invoke();

            cleanupActions.Clear();
            activeCoroutineCleanup = null;
            activeCoroutine = null;

            if (Instance == this)
                Instance = null;
        }

        // === TIERED SYSTEM ===
        bool TryStartCoroutine(IEnumerator coroutineMethod, int tier, Action? cleanup = null)
        {
            if (activeCoroutine != null)
            {
                if (tier < currentCoroutineTier)
                {
                    logger.LogDebug("A higher priority coroutine is already running, don't start this one");
                    return false;
                }

                wasInterrupted = true;
                StopCoroutine(activeCoroutine);

                if (activeCoroutineCleanup != null)
                {
                    activeCoroutineCleanup.Invoke();
                    cleanupActions.Remove(activeCoroutineCleanup);
                }

                activeCoroutine = null;
                activeCoroutineCleanup = null;
                currentCoroutineTier = -1;
            }

            wasInterrupted = false;

            if (cleanup != null)
            {
                cleanupActions.Add(cleanup);
                activeCoroutineCleanup = cleanup;
            }

            activeCoroutine = StartCoroutine(WrapTierCoroutine(coroutineMethod, tier, cleanup));
            currentCoroutineTier = tier;
            return true;
        }

        IEnumerator WrapTierCoroutine(IEnumerator coroutineMethod, int tier, Action? cleanup)
        {
            yield return coroutineMethod;

            if (!wasInterrupted && tier == currentCoroutineTier)
            {
                cleanup?.Invoke();
                cleanupActions.Remove(cleanup);

                // Your behavior switch here
                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.InActive);

                activeCoroutine = null;
                activeCoroutineCleanup = null;
                currentCoroutineTier = -1;
            }
        }

        // === NON-TIERED SYSTEM ===
        Coroutine StartSafeCoroutine(IEnumerator coroutineMethod, Action? cleanup = null)
        {
            return StartCoroutine(WrapSafeCoroutine(coroutineMethod, cleanup));
        }

        private IEnumerator WrapSafeCoroutine(IEnumerator coroutineMethod, Action? cleanup)
        {
            if (cleanup != null)
                cleanupActions.Add(cleanup);

            yield return coroutineMethod;

            if (cleanup != null)
            {
                cleanup.Invoke();
                cleanupActions.Remove(cleanup);
            }
        }

        public void RunAllEvents(float timeBetweenEvents)
        {
            IEnumerator RunAllEventsCoroutine(float timeBetweenEvents)
            {
                yield return null;

                List<MethodDelegate> allEvents = new List<MethodDelegate>();
                allEvents.AddRange(commonEvents);
                allEvents.AddRange(uncommonEvents);
                allEvents.AddRange(rareEvents);

                foreach (var _event in allEvents)
                {
                    _event.Invoke();
                    yield return new WaitForSeconds(timeBetweenEvents);
                }

                logger.LogDebug("All events run successfully");
            }

            StartCoroutine(RunAllEventsCoroutine(timeBetweenEvents));
        }

        public void RunRandomEvent(int eventRarity)
        {
            int eventIndex;

            switch (eventRarity)
            {
                case 0:
                    eventIndex = UnityEngine.Random.Range(0, commonEvents.Count);
                    //logger.LogDebug("Running common event 0 at index " + eventIndex);
                    commonEvents[eventIndex]?.Invoke();
                    break;
                case 1:
                    eventIndex = UnityEngine.Random.Range(0, uncommonEvents.Count);
                    //logger.LogDebug("Running uncommon event 1 at index " + eventIndex);
                    uncommonEvents[eventIndex]?.Invoke();
                    break;
                case 2:
                    StopAllCoroutines();
                    eventIndex = UnityEngine.Random.Range(0, rareEvents.Count);
                    //logger.LogDebug("Running rare event 2 at index " + eventIndex);
                    rareEvents[eventIndex]?.Invoke();
                    break;
                default:
                    break;
            }
        }

        public void RunEvent(int eventRarity, int eventIndex)
        {
            switch (eventRarity)
            {
                case 0:
                    //logger.LogDebug("Running common event 0 at index " + eventIndex);
                    commonEvents[eventIndex]?.Invoke();
                    break;
                case 1:
                    //logger.LogDebug("Running uncommon event 1 at index " + eventIndex);
                    uncommonEvents[eventIndex]?.Invoke();
                    break;
                case 2:
                    StopAllCoroutines();
                    //logger.LogDebug("Running rare event 2 at index " + eventIndex);
                    rareEvents[eventIndex]?.Invoke();
                    break;
                default:
                    break;
            }
        }

        #region Common

        public void FlickerLights() // 0 0
        {
            logger.LogDebug("FlickerLights");
            if (!localPlayer.isInsideFactory) { return; }
            RoundManager.Instance.FlickerLights(true, true);
            localPlayer.JumpToFearLevel(0.9f);
        }

        void PlayAmbientSFXNearby() // 0 1
        {
            logger.LogDebug("PlayAmbientSFXNearby");
            Vector3 pos = GetClosestNode(localPlayer.transform.position, !localPlayer.isInsideFactory).position;
            PlaySoundAtPosition(pos, SCP513_1AI.Instance!.AmbientSFX);
        }

        void PlayFakeSoundEffectMinor() // 0 2
        {
            logger.LogDebug("PlaySoundEffectMinor");

            // Filter clips that match inside/outside
            var clips = SCP513_1AI.Instance!.MinorSoundEffectSFX
                .Where(clip => clip.name.Length >= 2 &&
                               (clip.name[0] == 'I') == localPlayer.isInsideFactory)
                .ToArray();

            if (clips.Length == 0)
            {
                logger.LogWarning("No matching sound effects for current environment.");
                return;
            }

            // Pick one randomly
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];

            // Extract metadata from name
            bool is2D = clip.name[1] == '2';
            bool isFar = clip.name.Length > 3 && clip.name[3] == 'F';

            // Choose sound position based on 'F'
            int offset = isFar ? 5 : 0;
            Transform? pos = SCP513_1AI.Instance?.ChooseClosestNodeToPosition(localPlayer.transform.position, false, offset);
            if (pos == null) { logger.LogError("Couldnt find closest node to position"); return; }

            // Play the sound
            GameObject soundObj = Instantiate(SCP513_1AI.Instance!.SoundObjectPrefab, pos.position, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();
            source.spatialBlend = is2D ? 0f : 1f;
            source.clip = clip;
            source.Play();

            GameObject.Destroy(soundObj, clip.length);
        }

        void PlayBellSFX() // 0 3
        {
            logger.LogDebug("PlayBellSFX");
            Vector3 pos = GetClosestNode(localPlayer.transform.position, !localPlayer.isInsideFactory).position;
            PlaySoundAtPosition(pos, SCP513_1AI.Instance!.BellSFX);
        }

        void HideHazard() // 0 4
        {
            logger.LogDebug("HideHazard");

            float hideTime = 30f;

            Landmine? landmine = Utils.GetClosestGameObjectOfType<Landmine>(localPlayer.transform.position);
            Turret? turret = Utils.GetClosestGameObjectOfType<Turret>(localPlayer.transform.position);
            SpikeRoofTrap? spikeTrap = Utils.GetClosestGameObjectOfType<SpikeRoofTrap>(localPlayer.transform.position);

            var hazards = new List<(GameObject obj, float distance, string type)>();

            if (landmine != null)
                hazards.Add((landmine.gameObject, Vector3.Distance(localPlayer.transform.position, landmine.transform.position), "Landmine"));

            if (turret != null)
                hazards.Add((turret.gameObject, Vector3.Distance(localPlayer.transform.position, turret.transform.position), "Turret"));

            if (spikeTrap != null)
                hazards.Add((spikeTrap.gameObject, Vector3.Distance(localPlayer.transform.position, spikeTrap.transform.position), "SpikeTrap"));

            // If none found, return
            if (hazards.Count == 0)
            {
                RunRandomEvent(0);
                return;
            }

            // Get the closest
            var closest = hazards.OrderBy(h => h.distance).First();

            switch (closest.type)
            {
                case "Landmine": // Landmine

                    IEnumerator HideLandmineCoroutine(Landmine landmine)
                    {
                        logger.LogDebug("Hiding landmine");
                        yield return null;
                        landmine.GetComponent<MeshRenderer>().forceRenderingOff = true;

                        float elapsedTime = 0f;
                        while (elapsedTime < hideTime)
                        {
                            yield return new WaitForSeconds(0.2f);
                            elapsedTime += 0.2f;
                            if (landmine.localPlayerOnMine)
                            {
                                break;
                            }
                        }
                    }

                    Action cleanupLandmine = () =>
                    {
                        if (landmine != null)
                            landmine.GetComponent<MeshRenderer>().forceRenderingOff = false;
                    };

                    StartSafeCoroutine(HideLandmineCoroutine(landmine!), cleanupLandmine);

                break;
                case "Turret": // Turret

                    GameObject turretMesh = turret!.gameObject.transform.root.Find("MeshContainer").gameObject;

                    IEnumerator HideTurretCoroutine(Turret turret)
                    {
                        logger.LogDebug("Hiding turret");
                        yield return null;
                        turretMesh.SetActive(false);

                        float elapsedTime = 0f;
                        while (elapsedTime < hideTime)
                        {
                            yield return null;
                            elapsedTime += Time.deltaTime;
                            if (turret.turretMode != TurretMode.Detection)
                            {
                                break;
                            }
                        }
                    }

                    StartSafeCoroutine(HideTurretCoroutine(turret), () => turretMesh?.SetActive(true));

                    break;
                case "SpikeTrap": // SpikeTrap

                    MeshRenderer[] renderers = spikeTrap!.transform.root.GetComponentsInChildren<MeshRenderer>();

                    IEnumerator HideSpikeTrapCoroutine(SpikeRoofTrap spikeTrap)
                    {
                        try
                        {
                            logger.LogDebug("Hiding spike trap");
                            yield return null;
                            foreach (var renderer in renderers)
                            {
                                renderer.forceRenderingOff = true;
                            }

                            float elapsedTime = 0f;
                            while (elapsedTime < hideTime)
                            {
                                yield return new WaitForSeconds(0.2f);
                                elapsedTime += 0.2f;

                                if (localPlayer.isPlayerDead)
                                {
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            foreach (var renderer in renderers)
                            {
                                if (renderer == null) { continue; }
                                renderer.forceRenderingOff = false;
                            }
                        }
                    }

                    Action spikeTrapCleanup = () =>
                    {
                        foreach (var renderer in spikeTrap!.transform.root.GetComponentsInChildren<MeshRenderer>())
                        {
                            if (renderer == null) { continue; }
                            renderer.forceRenderingOff = false;
                        }
                    };

                    StartSafeCoroutine(HideSpikeTrapCoroutine(spikeTrap), spikeTrapCleanup);

                    break;
                default:
                break;
            }
        }
        
        void Stare() // 0 5
        {
            logger.LogDebug("Stare");

            IEnumerator StareCoroutine()
            {
                float stareTime = 15f;

                yield return null;

                Vector3 outsideLOS = SCP513_1AI.Instance!.GetRandomPositionAroundPlayer(5f, 15f, 10);

                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Manifesting);
                SCP513_1AI.Instance!.Teleport(outsideLOS);
                SCP513_1AI.Instance!.facePlayer = true;
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", true);
                RoundManager.PlayRandomClip(SCP513_1AI.Instance!.creatureSFX, SCP513_1AI.Instance!.AmbientSFX);

                float elapsedTime = 0f;

                while (elapsedTime < stareTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;

                    if (SCP513_1AI.Instance!.playerHasLOS)
                    {
                        yield return new WaitForSeconds(2.5f);
                        FlickerLights();
                        yield break;
                    }
                }
            }

            TryStartCoroutine(StareCoroutine(), 0);
        }

        #endregion

        #region UnCommon

        void FarStare() // 1 0
        {
            logger.LogDebug("FarStare");

            IEnumerator StareCoroutine()
            {
                float stareTime = 15f;

                yield return null;

                Transform? pos = SCP513_1AI.Instance!.TryFindingHauntPosition();
                while (pos == null)
                {
                    yield return new WaitForSeconds(0.2f);
                    pos = SCP513_1AI.Instance!.TryFindingHauntPosition();
                }

                if (UnityEngine.Random.Range(1, 4) == 1)
                {
                    LogStareArt();
                }

                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Manifesting);
                SCP513_1AI.Instance!.Teleport(pos.position);
                SCP513_1AI.Instance!.facePlayer = true;
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", true);
                RoundManager.PlayRandomClip(SCP513_1AI.Instance!.creatureSFX, SCP513_1AI.Instance!.AmbientSFX);

                float elapsedTime = 0f;

                while (elapsedTime < stareTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;

                    if (SCP513_1AI.Instance!.playerHasLOS)
                    {
                        yield return new WaitForSeconds(2.5f);
                        FlickerLights();
                        yield break;
                    }
                }
            }

            TryStartCoroutine(StareCoroutine(), 1);
        }

        void Jumpscare() // 1 1
        {
            logger.LogDebug("Jumpscare");

            IEnumerator JumpscareCoroutine()
            {
                float runSpeed = 20f;
                float disappearTime = 5f;

                yield return null;

                Transform? pos = SCP513_1AI.Instance!.ChoosePositionInFrontOfPlayer(1f, 5f);
                while (pos == null)
                {
                    yield return new WaitForSeconds(0.2f);
                    pos = SCP513_1AI.Instance!.ChoosePositionInFrontOfPlayer(1f, 5f);
                }

                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Chasing);
                SCP513_1AI.Instance!.Teleport(pos.position);
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", false);
                SCP513_1AI.Instance!.agent.speed = runSpeed;
                SCP513_1AI.Instance!.facePlayer = true;

                yield return new WaitForSeconds(disappearTime);
            }

            TryStartCoroutine(JumpscareCoroutine(), 1);
        }

        void BlockDoor() // 1 2
        {
            logger.LogDebug("BlockDoor");

            IEnumerator BlockDoorCoroutine()
            {
                float doorDistance = 10f;
                float blockPosOffset = 1f;
                float disappearDistance = 15f;
                float disappearTime = 15f;

                yield return null;

                DoorLock[] doorLocks = GetDoorLocksNearbyPosition(localPlayer.transform.position, doorDistance).ToArray();
                while (doorLocks.Length == 0)
                {
                    yield return new WaitForSeconds(1f);
                    doorLocks = GetDoorLocksNearbyPosition(localPlayer.transform.position, doorDistance).ToArray();
                }

                int index = UnityEngine.Random.Range(0, doorLocks.Length);
                DoorLock doorLock = doorLocks[index];

                var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
                Vector3 blockPos = RoundManager.Instance.GetNavMeshPosition(steelDoorObj.transform.position + Vector3.forward * blockPosOffset);

                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Manifesting);
                SCP513_1AI.Instance!.Teleport(blockPos);
                SCP513_1AI.Instance!.SetDestinationToPosition(blockPos);
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", true);
                SCP513_1AI.Instance!.facePlayer = true;

                float elapsedTime = 0f;
                while (elapsedTime < disappearTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;
                    if (Vector3.Distance(SCP513_1AI.Instance!.transform.position, localPlayer.transform.position) > disappearDistance || SCP513_1AI.Instance!.currentBehaviourState != SCP513_1AI.State.Manifesting)
                    {
                        break;
                    }
                }
            }

            TryStartCoroutine(BlockDoorCoroutine(), 1);
        }

        void StalkPlayer() // 1 3
        {
            logger.LogDebug("StalkPlayer");

            IEnumerator StalkCoroutine()
            {
                yield return null;

                Transform? teleportTransform = SCP513_1AI.Instance!.ChooseClosestNodeToPosition(localPlayer.transform.position, true);
                while (teleportTransform == null)
                {
                    yield return new WaitForSeconds(1f);
                    teleportTransform = SCP513_1AI.Instance!.ChooseClosestNodeToPosition(localPlayer.transform.position, true);
                }

                Vector3 teleportPos = teleportTransform.position;

                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Stalking);
                SCP513_1AI.Instance!.Teleport(teleportPos);
                SCP513_1AI.Instance!.SetDestinationToPosition(teleportPos);
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", false);

                while (SCP513_1AI.Instance!.currentBehaviourState == SCP513_1AI.State.Stalking && localPlayer.isInsideFactory != SCP513_1AI.Instance!.isOutside)
                {
                    yield return new WaitForSeconds(1f);
                }

                FlickerLights();
            }

            TryStartCoroutine(StalkCoroutine(), 1);
        }

        void PlayFakeSoundEffectMajor() // 1 4
        {
            logger.LogDebug("PlayFakeSoundEffectMajor");

            // Filter clips that match inside/outside
            var clips = SCP513_1AI.Instance!.MajorSoundEffectSFX
                .Where(clip => clip.name.Length >= 2 &&
                               (clip.name[0] == 'I') == localPlayer.isInsideFactory)
                .ToArray();

            if (clips.Length == 0)
            {
                logger.LogWarning("No matching sound effects for current environment.");
                return;
            }

            // Pick one randomly
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];

            // Extract metadata from name
            bool is2D = clip.name[1] == '2';
            bool isFar = clip.name.Length > 3 && clip.name[3] == 'F';

            // Choose sound position based on 'F'
            int offset = isFar ? 5 : 0;
            Vector3 pos = SCP513_1AI.Instance!.ChooseClosestNodeToPosition(localPlayer.transform.position, false, offset).position;

            // Play the sound
            GameObject soundObj = Instantiate(SCP513_1AI.Instance!.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();
            source.spatialBlend = is2D ? 0f : 1f;
            source.clip = clip;
            source.Play();

            GameObject.Destroy(soundObj, clip.length);
        }

        void ShowFakeShipLeavingDisplayTip() // 1 5
        {
            logger.LogDebug("ShowFakeShipLeavingDisplayTip");

            TimeOfDay.Instance.shipLeavingEarlyDialogue[0].bodyText = "WARNING! Please return by " + GetClock(TimeOfDay.Instance.normalizedTimeOfDay + 0.1f, TimeOfDay.Instance.numberOfHours, createNewLine: false) + ". A vote has been cast, and the autopilot ship will leave early.";
            HUDManager.Instance.ReadDialogue(TimeOfDay.Instance.shipLeavingEarlyDialogue);
        }

        void SpawnFakeBody() // 1 6
        {
            logger.LogDebug("SpawnFakeBody");

            float radius = 3f;

            int deathAnimation = UnityEngine.Random.Range(0, 8);

            Vector2 offset2D = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 randomOffset = new Vector3(offset2D.x, 0f, offset2D.y);

            GameObject bodyObj = SpawnDeadBody(localPlayer, localPlayer.transform.position, 0, deathAnimation, randomOffset);
            GameObject.Destroy(bodyObj, 30f);
        }

        void SlowWalkToPlayer() // 1 7
        {
            logger.LogDebug("SlowWalkToPlayer");

            IEnumerator SlowWalkCoroutine()
            {
                yield return null;

                Transform? teleportTransform = SCP513_1AI.Instance!.ChooseClosestNodeToPosition(localPlayer.transform.position, false, 3);
                while (teleportTransform == null)
                {
                    yield return new WaitForSeconds(1f);
                    teleportTransform = SCP513_1AI.Instance!.ChooseClosestNodeToPosition(localPlayer.transform.position, false, 3);
                }

                Vector3 teleportPos = teleportTransform.position;

                SCP513_1AI.Instance!.Teleport(teleportPos);
                SCP513_1AI.Instance!.agent.speed = 5f;
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", true);
                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Chasing);

                while (SCP513_1AI.Instance!.currentBehaviourState == SCP513_1AI.State.Chasing && localPlayer.isInsideFactory != SCP513_1AI.Instance!.isOutside)
                {
                    yield return new WaitForSeconds(5f);
                    FlickerLights();
                }
            }

            TryStartCoroutine(SlowWalkCoroutine(), 1);
        }

        #endregion

        #region Rare

        void MimicEnemyChase() // 2 0
        {
            logger.LogDebug("MimicEnemyChase");
            // See MimicableEnemies.txt
            string[] enemies = new string[]
            {
                "Flowerman",
                "SpringMan",
                "MaskedPlayerEnemy",
                "Crawler",
                "Butler"
            };

            int randomIndex = UnityEngine.Random.Range(0, enemies.Length);
            SCP513_1AI.Instance!.MimicEnemy(enemies[randomIndex]);
        }

        void MimicPlayer() // 2 1
        {
            logger.LogDebug("MimicPlayer");

            if (!localPlayer.NearOtherPlayers(localPlayer))
            {
                RunRandomEvent(2);
                return;
            }

            List<PlayerControllerB> ignoredPlayers = new List<PlayerControllerB> { localPlayer };
            PlayerControllerB[] nearByPlayers = Utils.GetNearbyPlayers(localPlayer.transform.position, 10f, ignoredPlayers);
            if (nearByPlayers.Length == 0)
            {
                RunRandomEvent(2);
                return;
            }

            int index = UnityEngine.Random.Range(0, nearByPlayers.Length);
            PlayerControllerB mimicPlayer = nearByPlayers[index].gameObject.GetComponent<PlayerControllerB>();

            IEnumerator MimicPlayerCoroutine(PlayerControllerB mimicPlayer)
            {
                yield return null;

                Utils.MakePlayerInvisible(mimicPlayer, true);

                yield return null;

                SCP513_1AI.Instance!.mimicPlayer = mimicPlayer;
                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.MimicPlayer);

                yield return new WaitForSeconds(30f);
            }

            TryStartCoroutine(MimicPlayerCoroutine(mimicPlayer), 2);
        }

        void ChasePlayer() // 2 2
        {
            logger.LogDebug("ChasePlayer");

            IEnumerator ChaseCoroutine()
            {
                yield return null;

                Transform? teleportPos = SCP513_1AI.Instance!.ChooseFarthestNodeFromPosition(localPlayer.transform.position);
                if (teleportPos == null) { logger.LogError("Couldnt find farthest node from position"); yield break; }

                SCP513_1AI.Instance!.Teleport(teleportPos.position);
                SCP513_1AI.Instance!.agent.speed = 10f;
                SCP513_1AI.Instance!.creatureAnimator.SetBool("armsCrossed", false);
                SCP513_1AI.Instance!.SwitchToBehavior(SCP513_1AI.State.Chasing);

                while (SCP513_1AI.Instance!.currentBehaviourState == SCP513_1AI.State.Chasing && localPlayer.isInsideFactory != SCP513_1AI.Instance!.isOutside)
                {
                    yield return new WaitForSeconds(2.5f);
                    FlickerLights();
                }
            }

            TryStartCoroutine(ChaseCoroutine(), 2);
        }

        void SpawnGhostGirl() // DressGirl // 2 3
        {
            logger.LogDebug("SpawnGhostGirl");

            NetworkHandlerHeavyItemSCPs.Instance?.SpawnGhostGirlServerRpc(localPlayer.actualClientId);
        }

        void TurnOffAllLights() // 2 4
        {
            logger.LogDebug("TurnOffAllLights");
            FlickerLights();
            RoundManager.Instance.TurnOnAllLights(false);
        }

        void SpawnFakeLandminesAroundPlayer() // 2 5
        {
            logger.LogDebug("SpawnFakeLandminesAroundPlayer");

            // Configs
            float spawnTime = 10f;

            Dictionary<string, GameObject> hazards = Utils.GetAllHazards();

            IEnumerator SpawnLandminesAroundPlayerCoroutine()
            {
                int minToSpawn = 5;
                int maxToSpawn = 20;

                int spawnAmount = UnityEngine.Random.Range(minToSpawn, maxToSpawn + 1);
                List<Vector3> positions = Utils.GetEvenlySpacedNavMeshPositions(localPlayer.transform.position, spawnAmount, 3f, 5f);

                foreach (Vector3 position in positions)
                {
                    yield return null;

                    GameObject landmineObj = GameObject.Instantiate(hazards["Landmine"], position, Quaternion.identity, RoundManager.Instance.mapPropsContainer.transform);
                    Landmine landmine = landmineObj.GetComponentInChildren<Landmine>();
                    landmine.mineActivated = true;
                    landmine.mineAudio.PlayOneShot(landmine.mineDeactivate);

                    IEnumerator DespawnLandmineConditionCoroutine(Landmine landmine)
                    {
                        yield return null;
                        float elapsedTime = 0f;

                        while (elapsedTime < spawnTime)
                        {
                            yield return null;
                            elapsedTime += Time.deltaTime;

                            if (landmine.localPlayerOnMine)
                            {
                                break;
                            }
                        }
                    }

                    StartSafeCoroutine(DespawnLandmineConditionCoroutine(landmine), () => GameObject.Destroy(landmine.gameObject));
                }
            }

            StartCoroutine(SpawnLandminesAroundPlayerCoroutine());
        }

        void SpawnMultipleFakeBodies() // 2 6
        {
            logger.LogDebug("SpawnMultipleFakeBodies");

            float radius = 3f;
            int minBodies = 5;
            int maxBodies = 10;

            int amount = UnityEngine.Random.Range(minBodies, maxBodies + 1);

            for (int i = 0; i < amount; i++)
            {
                int deathAnimation = UnityEngine.Random.Range(0, 8);

                Vector2 offset2D = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 randomOffset = new Vector3(offset2D.x, 0f, offset2D.y);

                GameObject bodyObj = SpawnDeadBody(localPlayer, localPlayer.transform.position, 0, deathAnimation, randomOffset);
                GameObject.Destroy(bodyObj, 30f);
            }
        }

        void ForceSuicide() // 2 7
        {
            logger.LogDebug("ForceSuicide");

            bool playerHasShotgun = false;
            bool playerHasKnife = false;
            bool playerHasMask = false;

            foreach (var slot in localPlayer.ItemSlots)
            {
                if (slot == null) { continue; }
                if (slot.itemProperties.name == "Shotgun")
                {
                    playerHasShotgun = true;
                }
                if (slot.itemProperties.name == "Knife")
                {
                    playerHasKnife = true;
                }
                if (slot.itemProperties.name == "ComedyMask" || slot.itemProperties.name == "TragedyMask")
                {
                    playerHasMask = true;
                }
            }

            if (!playerHasKnife && !playerHasShotgun && !playerHasMask)
            {
                RunRandomEvent(2);
                return;
            }

            IEnumerator ForceSuicideCoroutine(bool hasShotgun, bool hasMask)
            {
                yield return null;
                int itemSlotIndex = 0;

                if (hasShotgun) // Shotgun
                {
                    Utils.FreezePlayer(localPlayer, true);
                    localPlayer.activatingItem = true;
                    ShotgunItem? shotgun = null;

                    foreach (var slot in localPlayer.ItemSlots)
                    {
                        if (slot == null) { continue; }
                        if (slot.itemProperties.name == "Shotgun")
                        {
                            shotgun = (ShotgunItem)slot;
                            itemSlotIndex = localPlayer.ItemSlots.IndexOf(shotgun);
                        }
                    }

                    if (shotgun == null) { logger.LogError("Couldnt find shotgun"); yield break; }

                    localPlayer.SwitchToItemSlot(itemSlotIndex, shotgun);

                    NetworkHandlerHeavyItemSCPs.Instance?.ShotgunSuicideServerRpc(shotgun.NetworkObject, 5f);

                    yield return new WaitForSeconds(10f);
                }
                else if (hasMask) // Mask
                {
                    Utils.FreezePlayer(localPlayer, true);
                    localPlayer.activatingItem = true;
                    HauntedMaskItem? mask = null;
                    

                    foreach (var slot in localPlayer.ItemSlots)
                    {
                        if (slot == null) { continue; }
                        if (slot.itemProperties.name == "ComedyMask" || slot.itemProperties.name == "TragedyMask")
                        {
                            mask = (HauntedMaskItem)slot;
                            itemSlotIndex = localPlayer.ItemSlots.IndexOf(mask);
                        }
                    }

                    if (mask == null) { logger.LogError("Couldnt find mask"); yield break; }

                    localPlayer.SwitchToItemSlot(itemSlotIndex, mask);

                    yield return new WaitForSeconds(1f);

                    mask.maskOn = true;
                    localPlayer.activatingItem = true;
                    mask.BeginAttachment();

                    yield return new WaitForSeconds(1f);
                }
                else // Knife
                {
                    Utils.FreezePlayer(localPlayer, true);
                    localPlayer.activatingItem = true;
                    KnifeItem? knife = null;

                    foreach (var slot in localPlayer.ItemSlots)
                    {
                        if (slot == null) { continue; }
                        if (slot.itemProperties.name == "Knife")
                        {
                            knife = (KnifeItem)slot;
                            itemSlotIndex = localPlayer.ItemSlots.IndexOf(knife);
                        }
                    }

                    if (knife == null) { logger.LogError("Couldnt find knife"); yield break; }

                    localPlayer.SwitchToItemSlot(itemSlotIndex, knife);

                    float elapsedTime = 0f;

                    while (!localPlayer.isPlayerDead)
                    {
                        yield return null;
                        elapsedTime += Time.deltaTime;

                        Transform camTransform = localPlayer.gameplayCamera.transform;
                        Vector3 currentAngles = camTransform.localEulerAngles;
                        float targetX = 90f;
                        float smoothedX = Mathf.LerpAngle(currentAngles.x, targetX, Time.deltaTime * 5f);
                        camTransform.localEulerAngles = new Vector3(smoothedX, currentAngles.y, 0f);

                        if (elapsedTime > 1f)
                        {
                            elapsedTime = 0f;
                            knife.UseItemOnClient();
                            localPlayer.activatingItem = true;
                            yield return new WaitForSeconds(0.25f);
                            localPlayer.DamagePlayer(25, true, true, CauseOfDeath.Stabbing);
                        }
                    }
                }

                Utils.FreezePlayer(localPlayer, false);
            }

            Action cleanup = () => Utils.FreezePlayer(localPlayer, false);
            TryStartCoroutine(ForceSuicideCoroutine(playerHasShotgun, playerHasMask), 2, cleanup);
        }

        #endregion

        #region Miscellaneous

        public static void LogStareArt()
        {
            logger.LogInfo(@" I SEE YOU
;;;;;+++xXxxxxxxxxxxXXXXXXXXXxx+++++;;;;;::....:;::.................::
;+++++++xXXXXXXXxXXX$$$$$$$$$Xxx+++;;;+;:::...::::................:::.
+++++++xXXXXXXX$$X$$$$$$$$$$$XXxx+;;;;::::;::.........................
++++++xxX$$$$X$$$$$&$&&&&&$XXXXx+;;;;;;;;;;;::........................
+++++xxx$$$$$$$$XX$$&$$&&&&$$Xx++;;;;;;;++;;;::.....::..........:.....
++++xxxX$$$$$&&&$XXX$$XX$&$$&X;:::::;;:::::::::......:................
+++x+xxX$$$XxXX$x++x$$$&&$&&&X+;:::::::...........::..................
+++xxxxXXx+;;+;;;;;xxX$&&$xxXxxxx+;:...:x+;...........................
++xxxxXXX+;;::..::;;+xxX$$$X&&&$+;;::..;X$x:..........................
+++xxxXXXx+::.;X&&X;+x$&&$$&&&&&x;:::...::............................
+++++xxX$$X;:::+X$+++X$$&&&&&&$XX+;:...::::::;+;;;:...................
+x+xxxxX$&&$+;;:::;;+X&&&&&&$XXx;........:;+xx++;:....................
+xxxxxxxX$&&&$$XxxxxX$&&&&&&$XXx;:......::;+x+;+;:....:...............
++xxxxxxxX$&&&&&&X+x$&&&&&&&&Xxx;.........:;;;::.......:..............
+++xxxxxxxX$$&&&$XxXXX$XxX&&&$X+:.:;;::::::;;::.......................
++++xxxxx+xxXXXX$$$$xx$$+x$Xx++;;:.:::::;;:.:.........................
++++xxxxxxxxXXxX&&&$$&&&XX$Xx;;;;;::....:;;;:..........:..............
++xxxxxxxxxX$X++$&&&&&$$$$&&X+;::::::...::;;;;:.........:::...........
+xxXXXXXXx+xXXx+xX$&&&&&XX&&$x+;;:++;;;:;;;;:::;:.....................
+xxXXXXXXxxxxXXX$x;;;xX&&$$&&x+Xx;xx;;+;x+;:....::;;:::.....::::......
++xXXXXXXxXXxX$X$x:;:::;$$XXX;:+x:;;:;;::::.......:;+x+;;;;;;;::..::::
++xXXXXXXxXxxxxxx++;::..:;;::;::;:.................:;+;:::::::;;;;;;::
+xXXXXXXxxxxxxxxXXXX+;:....:........................::::..............
xxxXXxxxxxxXXxxXXXX&$x+:............................::::..............
xxxxXXXXxxxxXXXXXXX$&$Xx;:::........................:;:::::...........
xxxxXXXXXxxXxxxxxxxxX$$XX$x+:.......................:::;::............
XXXXXXXXxxxXXXxx++xXXXXX$&$Xx;;;;:::::...................::::.........
XXXXXXXXXxxxxxx++x++XXXXXX$$xXXxxX$X+;:...............................");
        }

        public void PlaySoundAtPosition(Vector3 pos, AudioClip clip, bool randomize = true, bool spatial3D = true)
        {
            GameObject soundObj = Instantiate(SCP513_1AI.Instance!.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();

            if (randomize)
            {
                source.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            }

            if (!spatial3D)
            {
                source.spatialBlend = 0f;
            }

            source.clip = clip;
            source.Play();
            GameObject.Destroy(soundObj, source.clip.length);
        }

        public void PlaySoundAtPosition(Vector3 pos, AudioClip[] clips, bool randomize = true, bool spatial3D = true)
        {
            GameObject soundObj = Instantiate(SCP513_1AI.Instance!.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();

            if (randomize)
            {
                source.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            }

            if (!spatial3D)
            {
                source.spatialBlend = 0f;
            }

            int index = UnityEngine.Random.Range(0, clips.Length);
            source.clip = clips[index];
            source.Play();
            GameObject.Destroy(soundObj, source.clip.length);
        }

        public static List<DoorLock> GetDoorLocksNearbyPosition(Vector3 pos, float distance)
        {
            List<DoorLock> doors = [];

            foreach (var door in GameObject.FindObjectsOfType<DoorLock>())
            {
                if (door == null) continue;
                if (Vector3.Distance(pos, door.transform.position) < distance)
                {
                    doors.Add(door);
                }
            }

            return doors;
        }

        public Transform GetClosestNode(Vector3 pos, bool outside = true)
        {
            GameObject[] nodes;
            if (outside && !Utils.inTestRoom)
            {
                if (RoundManager.Instance.outsideAINodes == null || RoundManager.Instance.outsideAINodes.Length <= 0)
                {
                    RoundManager.Instance.outsideAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
                    logger.LogDebug("Found OutsideAINodes: " + RoundManager.Instance.outsideAINodes.Length);
                }
                nodes = RoundManager.Instance.outsideAINodes;
            }
            else
            {
                if (RoundManager.Instance.insideAINodes == null || RoundManager.Instance.insideAINodes.Length <= 0)
                {
                    RoundManager.Instance.insideAINodes = GameObject.FindGameObjectsWithTag("AINode");
                    logger.LogDebug("Found AINodes: " + RoundManager.Instance.insideAINodes.Length);
                }
                nodes = RoundManager.Instance.insideAINodes;
            }

            logger.LogDebug("Node count: " + nodes.Length);
            float closestDistance = Mathf.Infinity;
            GameObject closestNode = null!;
            
            foreach (var node in nodes)
            {
                if (node == null) { continue; }
                float distance = Vector3.Distance(pos, node.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNode = node;
                }
            }

            return closestNode.transform;
        }

        public string GetClock(float timeNormalized, float numberOfHours, bool createNewLine = true)
        {
            string newLine;
            int num = (int)(timeNormalized * (60f * numberOfHours)) + 360;
            int num2 = (int)Mathf.Floor(num / 60);
            if (!createNewLine)
            {
                newLine = " ";
            }
            else
            {
                newLine = "\n";
            }
            string amPM = newLine + "AM";
            if (num2 >= 24)
            {
                return "12:00\nAM";
            }
            if (num2 < 12)
            {
                amPM = newLine + "AM";
            }
            else
            {
                amPM = newLine + "PM";
            }
            if (num2 > 12)
            {
                num2 %= 12;
            }
            int num3 = num % 60;
            string text = $"{num2:00}:{num3:00}".TrimStart('0') + amPM;
            return text;
        }

        public static GameObject SpawnDeadBody(PlayerControllerB deadPlayerController, Vector3 spawnPosition, int causeOfDeath = 0, int deathAnimation = 0, Vector3 positionOffset = default(Vector3))
        {
            float num = 1.32f;
            GameObject bodyObj = GameObject.Instantiate(deadPlayerController.playersManager.playerRagdolls[deathAnimation], spawnPosition + Vector3.up * num + positionOffset, Quaternion.identity);
            DeadBodyInfo deadBody = bodyObj.GetComponent<DeadBodyInfo>();
            deadBody.overrideSpawnPosition = true;
            if (deadPlayerController.physicsParent != null)
            {
                deadBody.SetPhysicsParent(deadPlayerController.physicsParent);
            }
            deadBody.parentedToShip = false;
            deadBody.playerObjectId = (int)deadPlayerController.actualClientId;
            for (int j = 0; j < deadPlayerController.bodyBloodDecals.Length; j++)
            {
                deadBody.bodyBloodDecals[j].SetActive(deadPlayerController.bodyBloodDecals[j].activeSelf);
            }
            ScanNodeProperties componentInChildren = deadBody.gameObject.GetComponentInChildren<ScanNodeProperties>();
            componentInChildren.headerText = "Body of " + deadPlayerController.playerUsername;
            CauseOfDeath causeOfDeath2 = (CauseOfDeath)causeOfDeath;
            componentInChildren.subText = "Cause of death: " + causeOfDeath2;
            deadBody.causeOfDeath = causeOfDeath2;
            if (causeOfDeath2 == CauseOfDeath.Bludgeoning || causeOfDeath2 == CauseOfDeath.Mauling || causeOfDeath2 == CauseOfDeath.Gunshots)
            {
                deadBody.MakeCorpseBloody();
            }
            return bodyObj;
        }

        #endregion
    }

    [HarmonyPatch]
    public class HallucinationManagerPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.LateUpdate))]
        public static bool LateUpdatePrefix(GrabbableObject __instance)
        {
            try
            {
                if (ScareManager.overrideShotguns.Contains(__instance))
                {
                    if (__instance.parentObject != null)
                    {
                        Vector3 rotOffset = ScareManager.overrideShotgunsRotOffsets[__instance];
                        Vector3 posOffset = ScareManager.overrideShotgunsPosOffsets[__instance];

                        __instance.transform.rotation = __instance.parentObject.rotation;
                        __instance.transform.Rotate(rotOffset);
                        __instance.transform.position = __instance.parentObject.position;
                        Vector3 positionOffset = posOffset;
                        positionOffset = __instance.parentObject.rotation * positionOffset;
                        __instance.transform.position += positionOffset;
                    }
                    if (__instance.radarIcon != null)
                    {
                        __instance.radarIcon.position = __instance.transform.position;
                    }
                    return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
*/