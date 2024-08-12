using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using DunGen;
using BepInEx;
using System.Threading;
using System;
using System.Linq;
using UnityEngine.SearchService;

namespace TaysonEnemy 
{
    // You may be wondering, how does the Example Enemy know it is from class TaysonEnemyAI?
    // Well, we give it a reference to to this class in the Unity project where we make the asset bundle.
    // Asset bundles cannot contain scripts, so our script lives here. It is important to get the
    // reference right, or else it will not find this file. See the guide for more information.

    class TaysonEnemyAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
        #region Variables
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        private float timeSinceHittingLocalPlayer = 0f;
        #pragma warning disable IDE0051, IDE0052, 0414
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        private Tile? entranceTile;
        private Tile? mostSpreadRoom;
        private float timeSinceLastCheck = 0f;
        private float timeBetweenChecks = 2f;
        private float wakeMeter = 0f; // 0-1
        private float hearNoiseCooldown = 0f;
        private bool inKillAnimation = false;
        private bool spottedPlayer = false;
        private float timeSinceSpottingPlayer = 0f;
        private AISearchRoutine circularSearch = new AISearchRoutine();
        private int attackCount = 0;
        float timeSinceLastPathfind = 0f;
        bool playersInDoors = false;
        private float lostLOSTimer = 0f;
        private bool lostPlayerInChase = false;
        private Transform? stareAtTransform;
        public Tile? EntranceTile
        {
            get => entranceTile;
            private set => entranceTile = value;
        }

        public Tile? MostSpreadRoom
        {
            get => mostSpreadRoom;
            private set => mostSpreadRoom = value;
        }
        #pragma warning restore IDE0051, IDE0052, 0414
        #endregion
        
        #region SFX
        public void SFX_Running()
        {
            if (Plugin.soundEffects == null)
            {
                UnityEngine.Debug.LogError("No running sound effect provided!");
                return;
            }
            creatureSFX.PlayOneShot(Plugin.soundEffects["running"], 0.15f);
        }
        public void SFX_Purring()
        {
            if (Plugin.soundEffects == null)
            {
                UnityEngine.Debug.LogError("No purring sound effect provided!");
                return;
            }
            if (isEnemyDead)
            {
                return;
            }
            creatureSFX.PlayOneShot(Plugin.soundEffects["purring"], 0.2f);
            
        }
        #endregion

        enum State 
        {
            GoToSleep,
            Sleep,
            WakeUp,
            Patrol,
            Stalking,
            Attack,
            Rage,
            Dead
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) 
        {
            Plugin.Logger.LogInfo(text);
        }
        
        public override void Start() 
        {
            base.Start();
            creatureSFX.volume = 1.5f;
            creatureVoice.volume = 2f;
            LogIfDebugBuild("Tayson Enemy Spawned");
            DoAnimationClientRpc("");
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            GoToSleep();
            enemyType.MaxCount = 1;
            StartCoroutine(SendData());
        }

        public override void Update() 
        {
            base.Update();
            if(isEnemyDead){
                return;
            }
            timeSinceHittingLocalPlayer += Time.deltaTime;
            hearNoiseCooldown -= Time.deltaTime;
            hearNoiseCooldown = hearNoiseCooldown < 0 ? 0 : hearNoiseCooldown;
            
        }

        public override void DoAIInterval() 
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) 
            {
                return;
            };
            
            switch(currentBehaviourStateIndex) {
                case (int)State.GoToSleep:
                    agent.speed = 12f;
                    wakeMeter = 0f;
                    if (Vector3.Distance(transform.position, favoriteSpot.position) <= 1f)
                    {
                        LogIfDebugBuild("       Going to sleep!");
                        Sleep();
                    }
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Running.Begin");
                    }
                    break;
                case (int)State.Sleep:
                    agent.speed = 0f;
                    if (timeSinceLastCheck >= timeBetweenChecks)
                    {
                        timeSinceLastCheck = 0f;
                        if (enemyRandom.NextDouble() < wakeMeter)
                        {

                            StartCoroutine(WakeUp());
                        }
                        if (wakeMeter > 0.1f)
                        {
                            wakeMeter -= 0.05f;
                        }
                        wakeMeter = Math.Max(0f, wakeMeter);
                    }
                    timeSinceLastCheck += AIIntervalTime;
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Sleeping.Begin");
                    }
                    break;
                case (int)State.Patrol:
                    agent.speed = 6f;
                    timeSinceSpottingPlayer += AIIntervalTime;
                    if (timeSinceSpottingPlayer > 5f)
                    {
                        spottedPlayer = false;
                        PlayerControllerB player = CheckLineOfSightForClosestPlayer(60);
                        if (player)
                        {
                            if (!player.HasLineOfSightToPosition(transform.position, 30))
                            {
                                agent.speed = 0f;
                                DoAnimationClientRpc("Walking.End");
                                StartCoroutine(SpottedPlayer(player));
                            }
                        }
                    }
                    if (currentSearch.unsearchedNodes.Count <= 0)
                    {
                        CircularSearch();
                    }
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Walking.Begin");
                    }
                    break;
                case (int)State.Stalking:
                    agent.speed = 10f;
                    if (Vector3.Distance(targetPlayer.transform.position, transform.position) < 4f)
                    {
                        SwitchToBehaviourClientRpc((int)State.Attack);
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        attackCount = 0;
                    }
                    else if (Time.realtimeSinceStartup - timeSinceLastPathfind > 1f)
                    {
                        if (PathIsIntersectedByLineOfSight(destination))
                        {
                            AvoidClosestPlayer();
                        }
                        else if (!SetDestinationToPosition(targetPlayer.transform.position, true))
                        {
                            SwitchToBehaviourClientRpc((int)State.Patrol);
                            DoAnimationClientRpc("Running.Stop");
                            CircularSearch();
                        }
                        else
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                            destination += Vector3.Normalize(targetPlayer.thisController.velocity * 100f) * -1.5f;
                        }
                    }
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Running.Begin");
                    }
                    break;
                case (int)State.Attack:
                    PlayerControllerB playerControllerB = CheckLineOfSightForClosestPlayer(60f, 50, 1, 3f);
                    agent.speed = 12f;
                    if (attackCount >= 5)
                    {
                        GoToSleep();
                    }
                    if (playerControllerB != null)
                    {
                        stareAtTransform = playerControllerB.gameplayCamera.transform;
                        lostPlayerInChase = false;
                        lostLOSTimer = 0f;
                        if (playerControllerB != targetPlayer)
                        {
                            SetMovingTowardsTargetPlayer(playerControllerB);
                            LookAtPlayerServerRpc((int)playerControllerB.playerClientId);
                        }
                    }
                    lostLOSTimer += AIIntervalTime;
                    if (lostLOSTimer > 10f)
                    {
                        SwitchToBehaviourClientRpc((int)State.Patrol);
                    }
                    else if (lostLOSTimer > 3.5f)
                    {
                        lostPlayerInChase = true;
                        StopLookingAtTransformServerRpc();
                        targetPlayer = null;
                    }
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Running.Begin");
                    }
                    break;
                case (int)State.Rage:
                    agent.speed = 16f;
                    agent.angularSpeed = 240 * 2f;
                    agent.acceleration = 8 * 3f;
                    playersInDoors = false;
                    foreach (PlayerControllerB pcb in StartOfRound.Instance.allPlayerScripts)
                    {
                        if (pcb.isInsideFactory)
                        {
                            playersInDoors = true;
                            break;
                        }
                    }
                    if (!playersInDoors)
                    {
                        GoToSleep();
                        agent.angularSpeed = 240;
                        agent.acceleration = 8f;
                    }
                    else
                    {
                        TargetClosestPlayer();
                        movingTowardsTargetPlayer = true;
                    }
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Running.Begin");
                    }
                    break;
                case (int)State.Dead:
                    creatureSFX.volume = 0;
                    creatureVoice.volume = 0;
                    agent.speed = 0;
                    if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name == "Idle")
                    {
                        DoAnimationClientRpc("Sleeping.Begin");
                    }
                    else if (creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name.Split('.')[0] != "Sleeping")
                    {
                        EndAnimation();
                    }
                    break;
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    LogIfDebugBuild($"State is: {currentBehaviourState.name}");
                    break;
            }
        }

        public IEnumerator SendData()
        {
            while (true)
            {
                yield return new WaitForSeconds(2f);
                LogIfDebugBuild($"Distance to the player: {Vector3.Distance(transform.position, RoundManager.Instance.playersManager.allPlayerObjects[0].transform.position)}");
                LogIfDebugBuild($"Current state: {currentBehaviourState.name}, {currentBehaviourStateIndex}");
                LogIfDebugBuild($"Current animation {creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name}");
                if ((int)State.Sleep == currentBehaviourStateIndex)
                {
                    LogIfDebugBuild($"Wake value is {wakeMeter}");
                }
                if ((int)State.Attack == currentBehaviourStateIndex)
                {
                    LogIfDebugBuild($"Attacks completed: {attackCount}, Time since collision: {timeSinceHittingLocalPlayer}");
                }
                if ((int)State.Patrol == currentBehaviourStateIndex)
                {
                    LogIfDebugBuild($"The number of unsearched nodes: {currentSearch.unsearchedNodes.Count}");
                    LogIfDebugBuild($"Time since spotting a player: {timeSinceSpottingPlayer}");
                    
                }
            }
        }
        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
            if (noiseID == 7 || noiseID == 546 || (int)State.Rage == currentBehaviourStateIndex ||
                (int)State.GoToSleep == currentBehaviourStateIndex || hearNoiseCooldown > 0f)
            {
                return;
            }
            hearNoiseCooldown = 0.03f;
            float dist = Vector3.Distance(base.transform.position, noisePosition);
            if (Physics.Linecast(base.transform.position, noisePosition, 256))
            {
                noiseLoudness /= 2f;
            }
            if (noiseLoudness < 0.25f)
            {
                return;
            }
            if ((int)State.Sleep == currentBehaviourStateIndex)
                wakeMeter += 0.065f * noiseLoudness / dist;
            if ((int)State.Patrol == currentBehaviourStateIndex)
            {
                targetNode = ChooseClosestNodeToPosition(noisePosition);   
            }
        }

        public void AttackPlayer()
        {
            switch (enemyRandom.Next(0, 2))
            {
                case 0:
                    DoAnimationClientRpc("Attack.1");
                    if (currentBehaviourStateIndex == (int)State.Attack) attackCount++;
                    break;
                case 1:
                    DoAnimationClientRpc("Attack.2");
                    if (currentBehaviourStateIndex == (int)State.Attack) attackCount++;
                    break;
                default:
                    LogIfDebugBuild("Somehow got not a number between 0-1 in the Attack state of the aiupdate");
                    break;
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            if ((int)State.GoToSleep == currentBehaviourStateIndex || (int)State.WakeUp == currentBehaviourStateIndex)
            {
                return;
            }
            if ((int)State.Sleep == currentBehaviourStateIndex)
            {
                wakeMeter = 1f;
                return;
            }
            if (timeSinceHittingLocalPlayer < 1f) 
            {
                return;
            }
            if (currentBehaviourStateIndex == (int)State.Attack || currentBehaviourStateIndex == (int)State.Rage && creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name != "Attack.1" && creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name != "Attack.2")
            {
                AttackPlayer();
            }
            base.OnCollideWithPlayer(other);
            
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other, inKillAnimation);
            if (currentBehaviourStateIndex == (int)State.Patrol)
            {
                AttackPlayer();
                SwitchToBehaviourClientRpc((int)State.Attack);
                SetMovingTowardsTargetPlayer(playerControllerB);
                attackCount = 0;
            }
            if (playerControllerB != null)
            {
                LogIfDebugBuild("Tayson enemy Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                
                if ((int)State.Rage == currentBehaviourStateIndex)
                {   
                    playerControllerB.DamagePlayer(50);
                }
                else
                {
                    playerControllerB.DamagePlayer(25);
                }
            }
        }
        void GoToSleep()
        {
            if (favoriteSpot == null)
            {
                if (RoundManager.Instance.dungeonGenerator == null)
                {
                    Plugin.Logger.LogWarning("dungeonGenerator instance is not present!");
                    favoriteSpot = this.transform;
                    targetNode = favoriteSpot;
                    SwitchToBehaviourClientRpc((int)State.GoToSleep);
                    return;
                }
                ReadOnlyCollection<Tile> tiles = RoundManager.Instance.dungeonGenerator.Generator.CurrentDungeon.AllTiles;
                if (tiles  == null)
                {
                    Plugin.Logger.LogError("Dungeon has not generated yet!");
                    return;
                } 
                mostSpreadRoom = tiles[0];
                foreach (Tile t in tiles)
                {
                    if (t.Placement.Depth == 0)
                    {
                        entranceTile = t;
                    }
                    else
                    {
                        if (t.UsedDoorways.Count >= mostSpreadRoom.UsedDoorways.Count)
                        {
                            mostSpreadRoom = t;
                        }
                    }
                }
                if (entranceTile == null)
                {
                    Plugin.Logger.LogWarning("Failed to find the Main tile");
                }
                Transform transform = ChooseClosestNodeToPosition(mostSpreadRoom.Placement.Position);
                favoriteSpot = transform;
                
            }
            if (!SetDestinationToPosition(favoriteSpot.position, checkForPath: true))
            {
                Plugin.Logger.LogWarning("Failed to pathfind to the favorite spot");
                favoriteSpot = transform;
                return;
            }
            else
            {
                LogIfDebugBuild("Succesfully found a path to the favorite location!");
            }
            targetNode = favoriteSpot;
            SwitchToBehaviourClientRpc((int)State.GoToSleep);
        }

        public IEnumerator SpottedPlayer(PlayerControllerB player)
        {
            targetPlayer = null;
            timeSinceSpottingPlayer = 0f;
            spottedPlayer = true;
            LookAtPlayerServerRpc((int)player.playerClientId);
            yield return new WaitForSeconds(2.5f);
            if (player.HasLineOfSightToPosition(transform.position, 30))
            {
                spottedPlayer = false;
            }
            else
            {
                SwitchToBehaviourClientRpc((int)State.Stalking);
                DoAnimationClientRpc("Walking.End");
                targetPlayer = player;
            }
        }

        void Sleep()
        {
            agent.speed = 0f;
            DoAnimationClientRpc("Running.End");
            SwitchToBehaviourClientRpc((int)State.Sleep);
            wakeMeter = 0f;
        }

        public IEnumerator WakeUp()
        {
            agent.speed = 0f;
            SwitchToBehaviourClientRpc((int)State.WakeUp);
            DoAnimationClientRpc("Sleeping.End");
            yield return new WaitForSeconds(2f);
            SwitchToBehaviourClientRpc((int)State.Patrol);
        }

        public int Compare(GameObject x, GameObject y)
        {
            return Vector3.Distance(x.transform.position, transform.position).
            CompareTo(Vector3.Distance(y.transform.position, transform.position));
            
        }

        public void CircularSearch()
        {
            circularSearch.loopSearch = false;
            circularSearch.unsearchedNodes = allAINodes.ToList();
            circularSearch.unsearchedNodes.Sort(Compare);
            base.StartSearch(transform.position, circularSearch);
        }

        public void AvoidClosestPlayer()
        {
            Transform transform = ChooseFarthestNodeFromPosition(targetPlayer.transform.position, avoidLineOfSight: true, 0, log: true);
            if (transform != null && mostOptimalDistance > 5f && Physics.Linecast(transform.transform.position, targetPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                targetNode = transform;
                SetDestinationToPosition(targetNode.position);
                return;
            }
            agent.speed = 0f;
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            LogIfDebugBuild("Tayson was hit!");
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if(isEnemyDead){
                return;
            }
            enemyHP -= force;
            if ((int)State.Rage != currentBehaviourStateIndex)
            {
                SwitchToBehaviourClientRpc((int)State.Rage);
                EndAnimation();
            }
            if (IsOwner) {
                if (enemyHP <= 0 && !isEnemyDead) {
                    // Our death sound will be played through creatureVoice when KillEnemy() is called.
                    // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
                    // so we don't need to call a death animation ourselves.

                    // We need to stop our search coroutine, because the game does not do that by default.
                    if (searchCoroutine != null)
                        StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                }
            }
        }
        
        public void EndAnimation()
        {
            string[] clipName = creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            LogIfDebugBuild($"Clip name is {clipName[0]}");
            if (clipName.Length == 2)
            {
                LogIfDebugBuild($"Clip postfix is {clipName[1]}");
            }
            if (clipName.Length == 1)
            {
                if (clipName[0] != "idle")
                {
                    DoAnimationClientRpc(clipName[0] + ".End");
                }
            }
            else if (clipName[1] == "begin")
            {
                executeAfterTimer(EndAnimation, 2.01f);
            }
        }
        
        public IEnumerator executeAfterTimer(Action doSomething, float afterTime)
        {
            yield return new WaitForSeconds(afterTime);
            doSomething();
        }

        public override void KillEnemy(bool destroy = false)
        {
            SwitchToBehaviourStateOnLocalClient((int)State.Dead);
            base.KillEnemy(destroy);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName) 
        {
            LogIfDebugBuild($"Animation: {animationName}");
            creatureAnimator.SetTrigger(animationName);
            LogIfDebugBuild($"Current animation: {creatureAnimator.GetCurrentAnimatorClipInfo(0)[0].clip.name}");
        }
        [ServerRpc]
        public void LookAtPlayerServerRpc(int playerId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
            {
                if (base.OwnerClientId != networkManager.LocalClientId)
                {
                    if (networkManager.LogLevel <= LogLevel.Normal)
                    {
                        UnityEngine.Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
                    }
                    return;
                }
                ServerRpcParams serverRpcParams = default(ServerRpcParams);
                FastBufferWriter bufferWriter = __beginSendServerRpc(1141953697u, serverRpcParams, RpcDelivery.Reliable);
                BytePacker.WriteValueBitPacked(bufferWriter, playerId);
                __endSendServerRpc(ref bufferWriter, 1141953697u, serverRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                LookAtPlayerClientRpc(playerId);
            }
        }
        [ClientRpc]
        public void LookAtPlayerClientRpc(int playerId)
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager != null && networkManager.IsListening)
            {
                if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
                {
                    ClientRpcParams clientRpcParams = default(ClientRpcParams);
                    FastBufferWriter bufferWriter = __beginSendClientRpc(2397761797u, clientRpcParams, RpcDelivery.Reliable);
                    BytePacker.WriteValueBitPacked(bufferWriter, playerId);
                    __endSendClientRpc(ref bufferWriter, 2397761797u, clientRpcParams, RpcDelivery.Reliable);
                }
                if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
                {
                    stareAtTransform = StartOfRound.Instance.allPlayerScripts[playerId].gameplayCamera.transform;
                }
            }
}
        [ServerRpc]
        public void StopLookingAtTransformServerRpc()
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager == null || !networkManager.IsListening)
            {
                return;
            }
            if (__rpc_exec_stage != __RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
            {
                if (base.OwnerClientId != networkManager.LocalClientId)
                {
                    if (networkManager.LogLevel <= LogLevel.Normal)
                    {
                        UnityEngine.Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
                    }
                    return;
                }
                ServerRpcParams serverRpcParams = default(ServerRpcParams);
                FastBufferWriter bufferWriter = __beginSendServerRpc(1407409549u, serverRpcParams, RpcDelivery.Reliable);
                __endSendServerRpc(ref bufferWriter, 1407409549u, serverRpcParams, RpcDelivery.Reliable);
            }
            if (__rpc_exec_stage == __RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
            {
                StopLookingAtTransformClientRpc();
            }
        }
        [ClientRpc]
        public void StopLookingAtTransformClientRpc()
        {
            NetworkManager networkManager = base.NetworkManager;
            if ((object)networkManager != null && networkManager.IsListening)
            {
                if (__rpc_exec_stage != __RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
                {
                    ClientRpcParams clientRpcParams = default(ClientRpcParams);
                    FastBufferWriter bufferWriter = __beginSendClientRpc(1561581057u, clientRpcParams, RpcDelivery.Reliable);
                    __endSendClientRpc(ref bufferWriter, 1561581057u, clientRpcParams, RpcDelivery.Reliable);
                }
                if (__rpc_exec_stage == __RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
                {
                    stareAtTransform = null;
                }
            }
        }

    }
}