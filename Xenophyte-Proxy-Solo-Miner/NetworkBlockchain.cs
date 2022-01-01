﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xenophyte_Connector_All.Seed;
using Xenophyte_Connector_All.Setting;
using Xenophyte_Connector_All.SoloMining;
using Xenophyte_Connector_All.Utils;

namespace Xenophyte_Proxy_Solo_Miner
{
    public class ClassMinerStats
    {
        public bool MinerConnectionStatus;
        public int MinerTotalGoodShare;
        public int MinerTotalShare;
        public string MinerVersion;
        public int MinerTotalInvalidShare;
        public int MinerDifficultyStart;
        public int MinerDifficultyEnd;
        public long MinerLastBlockTemplateReceived;
        public string MinerHashrateExpected;
        public string MinerHashrateCalculated;
        public string MinerIp;
        public string MinerName;
    }

    public class NetworkBlockchain
    {
        private static ClassSeedNodeConnector _classSeedNodeConnector;
        public static bool IsConnected;
        public static string Blocktemplate;
        public static int TotalBlockUnlocked;
        public static int TotalBlockWrong;
        public static List<string> ListOfMiningMethodName = new List<string>();
        public static List<string> ListOfMiningMethodContent = new List<string>();
        private static long _lastPacketReceivedFromBlockchain;

        private static CancellationTokenSource _cancellationTokenListenNetwork;

        /// <summary>
        /// Blockchain informations.
        /// </summary>
 		public static string CurrentBlockId;
        public static string CurrentBlockHash;
        public static string CurrentBlockAlgorithm;
        public static string CurrentBlockSize;
        public static string CurrentBlockMethod;
        public static string CurrentBlockKey;
        public static string CurrentBlockJob;
        public static string CurrentBlockReward;
        public static string CurrentBlockDifficulty;
        public static string CurrentBlockTimestampCreate;
        public static string CurrentBlockIndication;
        public static string CurrentBlockNetworkHashrate;
        public static string CurrentBlockLifetime;
        public static bool FirstStart;
        public static bool LoginAccepted;

        /// <summary>
        /// For calculate hashrate;
        /// </summary>
        public static int FirstBlockId;

        /// <summary>
        /// List of Miner stats.
        /// </summary>
        public static Dictionary<string, ClassMinerStats> ListMinerStats = new Dictionary<string, ClassMinerStats>();

        /// <summary>
        /// Connect to the network of blockchain.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> ConnectToBlockchainAsync()
        {

            _classSeedNodeConnector?.DisconnectToSeed();
            _classSeedNodeConnector = null;
            _classSeedNodeConnector = new ClassSeedNodeConnector();

            StopTokenListenNetwork();

            _cancellationTokenListenNetwork = new CancellationTokenSource();

            ListOfMiningMethodName?.Clear();
            ListOfMiningMethodContent?.Clear();
            if (!await _classSeedNodeConnector.StartConnectToSeedAsync(string.Empty))
            {
                IsConnected = false;
                return false;
            }
            if (!FirstStart)
            {
                FirstStart = true;
                CheckBlockchainConnection();
            }
            IsConnected = true;
            return true;
        }

        private static void StopTokenListenNetwork()
        {
            try
            {
                if (_cancellationTokenListenNetwork != null)
                {
                    if (!_cancellationTokenListenNetwork.IsCancellationRequested)
                    {
                        _cancellationTokenListenNetwork.Cancel();
                    }
                }
            }
            catch
            {

            }
        }

        private static void CheckBlockchainConnection()
        {
            _lastPacketReceivedFromBlockchain = DateTimeOffset.Now.ToUnixTimeSeconds();
            Task.Factory.StartNew(async delegate ()
            {
                while(true)
                {
                    await Task.Delay(1000);
                    if (!IsConnected || !_classSeedNodeConnector.ReturnStatus())
                    {
                        StopTokenListenNetwork();
                        IsConnected = false;
                        LoginAccepted = false;
                        NetworkProxy.StopProxy();
                        while (!await ConnectToBlockchainAsync())
                        {
                            ConsoleLog.WriteLine("Can't connect to the network, retry in 5 seconds..", ClassConsoleColorEnumeration.IndexConsoleRedLog);
                            await Task.Delay(5000);
                        }
                        ConsoleLog.WriteLine("Connection success, generate dynamic certificate for the network.", ClassConsoleColorEnumeration.IndexConsoleGreenLog);
                        Program.NetworkCertificate = ClassUtils.GenerateCertificate();
                        ConsoleLog.WriteLine("Certificate generate, send to the network..", ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                        if (!await SendPacketAsync(Program.NetworkCertificate, false))
                        {
                            ConsoleLog.WriteLine("Can't send certificate, reconnect now..", ClassConsoleColorEnumeration.IndexConsoleRedLog);
                            IsConnected = false;
                            StopTokenListenNetwork();
                        }
                        else
                        {
                            await Task.Delay(1000);
                            ConsoleLog.WriteLine("Certificate sent, start to login..", ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                            ListenBlockchain();
                            if (!await SendPacketAsync(ClassConnectorSettingEnumeration.MinerLoginType+ClassConnectorSetting.PacketContentSeperator + Config.WalletAddress, true))
                            {
                                ConsoleLog.WriteLine("Can't login to the network, reconnect now.", ClassConsoleColorEnumeration.IndexConsoleRedLog);
                                IsConnected = false;
                                StopTokenListenNetwork();
                            }
                            else
                            {
                                ConsoleLog.WriteLine("Login successfully sent, waiting confirmation.. (Wait 5 seconds maximum.)", ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                IsConnected = true;
                                await Task.Delay(ClassConnectorSetting.MaxTimeoutConnect);
                                if (!LoginAccepted)
                                {
                                    IsConnected = false;
                                    StopTokenListenNetwork();
                                }
                            }
                        }
                    }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
        }


        /// <summary>
        /// Listen Blockchain packet.
        /// </summary>
        public static void ListenBlockchain()
        {
            try
            {
                Task.Factory.StartNew(async delegate()
                    {
                        while (IsConnected)
                        {
                            try
                            {

                                string packet =
                                    await _classSeedNodeConnector.ReceivePacketFromSeedNodeAsync(
                                        Program.NetworkCertificate,
                                        false, true);
                                if (packet == ClassSeedNodeStatus.SeedError)
                                {
                                    ConsoleLog.WriteLine("Connection to network lost, reconnect in 5 seconds..",
                                        ClassConsoleColorEnumeration.IndexConsoleRedLog);
                                    IsConnected = false;
                                    break;
                                }

                                _lastPacketReceivedFromBlockchain = DateTimeOffset.Now.ToUnixTimeSeconds();

                                if (packet.Contains(ClassConnectorSetting.PacketSplitSeperator))
                                {
                                    var splitPacket = packet.Split(new[] {ClassConnectorSetting.PacketSplitSeperator}, StringSplitOptions.None);
                                    if (splitPacket.Length > 1)
                                    {
                                        foreach (var packetEach in splitPacket)
                                        {
                                            if (!string.IsNullOrEmpty(packetEach))
                                            {

                                                if (!await HandlePacketBlockchainAsync(packetEach.Replace(ClassConnectorSetting.PacketSplitSeperator, "")))
                                                {
                                                    IsConnected = false;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (!await HandlePacketBlockchainAsync(packet.Replace(ClassConnectorSetting.PacketSplitSeperator, "")))
                                        {
                                            IsConnected = false;
                                        }
                                    }
                                }
                                else
                                {
                                    if (!await HandlePacketBlockchainAsync(packet))
                                    {
                                        IsConnected = false;
                                    }
                                }

                            }
                            catch
                            {
                                ConsoleLog.WriteLine("Connection to network lost, reconnect in 5 seconds..",
                                    ClassConsoleColorEnumeration.IndexConsoleRedLog);
                                IsConnected = false;
                                break;
                            }
                        }
                    }, _cancellationTokenListenNetwork.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current)
                    .ConfigureAwait(false);
            }
            catch
            {

            }
        }

        /// <summary>
        /// Receive packet from the blockchain.
        /// </summary>
        /// <param name="packet"></param>
        private static async Task<bool> HandlePacketBlockchainAsync(string packet)
        {
            var splitPacket = packet.Split(new[] { ClassConnectorSetting.PacketContentSeperator }, StringSplitOptions.None);
            switch(splitPacket[0])
            {
                case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendLoginAccepted:
                    LoginAccepted = true;
                    IsConnected = true;
                    ConsoleLog.WriteLine("Proxy login accepted, ask mining methods.", ClassConsoleColorEnumeration.IndexConsoleGreenLog);
                    if (!NetworkProxy.ProxyStarted)
                    {
                        NetworkProxy.StartProxy();
                    }
                    AskMiningMethod();
                    break;
                case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendListBlockMethod:
                    var methodList = splitPacket[1];
                    await Task.Factory.StartNew(async () =>
                        {
                            if (methodList.Contains("#"))
                            {
                                var splitMethodList = methodList.Split(new[] {"#"}, StringSplitOptions.None);
                                if (ListOfMiningMethodName.Count > 1)
                                {
                                    foreach (var methodName in splitMethodList)
                                    {
                                        if (!string.IsNullOrEmpty(methodName))
                                        {
                                            if (ListOfMiningMethodName.Contains(methodName) == false)
                                            {
                                                ListOfMiningMethodName.Add(methodName);
                                            }

                                            if (!await _classSeedNodeConnector.SendPacketToSeedNodeAsync(
                                                ClassSoloMiningPacketEnumeration.SoloMiningSendPacketEnumeration
                                                    .ReceiveAskContentBlockMethod +
                                                ClassConnectorSetting.PacketContentSeperator + methodName + ClassConnectorSetting.PacketMiningSplitSeperator,
                                                Program.NetworkCertificate, false, true).ConfigureAwait(false))
                                            {
                                                IsConnected = false;
                                                break;
                                            }

                                            await Task.Delay(1000);
                                        }
                                    }
                                }
                                else
                                {

                                    foreach (var methodName in splitMethodList)
                                    {
                                        if (!string.IsNullOrEmpty(methodName))
                                        {
                                            if (ListOfMiningMethodName.Contains(methodName) == false)
                                            {
                                                ListOfMiningMethodName.Add(methodName);
                                            }

                                            if (!await _classSeedNodeConnector.SendPacketToSeedNodeAsync(
                                                ClassSoloMiningPacketEnumeration.SoloMiningSendPacketEnumeration
                                                    .ReceiveAskContentBlockMethod +
                                                ClassConnectorSetting.PacketContentSeperator + methodName + ClassConnectorSetting.PacketMiningSplitSeperator,
                                                Program.NetworkCertificate, false, true).ConfigureAwait(false))
                                            {
                                                IsConnected = false;
                                                break;
                                            }

                                            await Task.Delay(1000);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (ListOfMiningMethodName.Contains(methodList) == false)
                                {
                                    ListOfMiningMethodName.Add(methodList);
                                }

                                if (!await _classSeedNodeConnector.SendPacketToSeedNodeAsync(
                                    ClassSoloMiningPacketEnumeration.SoloMiningSendPacketEnumeration
                                        .ReceiveAskContentBlockMethod + ClassConnectorSetting.PacketContentSeperator +
                                    methodList + ClassConnectorSetting.PacketMiningSplitSeperator, Program.NetworkCertificate, false, true).ConfigureAwait(false))
                                {
                                    IsConnected = false;
                                }
                            }
                        }, _cancellationTokenListenNetwork.Token, TaskCreationOptions.LongRunning,
                        TaskScheduler.Current)
                        .ConfigureAwait(false);
                    break;
                case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendContentBlockMethod:
                    if (ListOfMiningMethodContent.Count == 0)
                    {
                        ListOfMiningMethodContent.Add(splitPacket[1]);
                    }
                    else
                    {
                        ListOfMiningMethodContent[0] = splitPacket[1];
                    }
                    break;
                case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendCurrentBlockMining:
                    var splitBlockContent = splitPacket[1].Split(new[] { "&" }, StringSplitOptions.None);

                    if (Blocktemplate != splitPacket[1])
                    {
                        ConsoleLog.WriteLine("New block to mining: " + splitBlockContent[0], ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                        Blocktemplate = splitPacket[1];
                        await Task.Factory.StartNew(() => SpreadJobAsync(), CancellationToken.None, TaskCreationOptions.RunContinuationsAsynchronously, TaskScheduler.Current).ConfigureAwait(false);
                    }
                    break;
                case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendJobStatus:
                    switch (splitPacket[1])
                    {
                        case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareUnlock:
                            TotalBlockUnlocked++;
                            ConsoleLog.WriteLine("Block accepted, stop mining, wait new block.", ClassConsoleColorEnumeration.IndexConsoleGreenLog);
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                    {
                                        if(!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendJobStatus + ClassConnectorSetting.PacketContentSeperator + ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareUnlock).ConfigureAwait(false))
                                        {
                                            NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                        }
                                        
                                    }
                                }
                            }
                            break;
                        case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareWrong:
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                    {
                                        if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendJobStatus + ClassConnectorSetting.PacketContentSeperator + ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareWrong).ConfigureAwait(false))
                                        {
                                            NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                        }

                                    }
                                }
                            }
                            TotalBlockWrong++;
                            ConsoleLog.WriteLine("Block not accepted, stop mining, wait new block.", ClassConsoleColorEnumeration.IndexConsoleRedLog);
                            break;
                        case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareBad:
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                    {
                                        if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendJobStatus + ClassConnectorSetting.PacketContentSeperator + ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareBad).ConfigureAwait(false))
                                        {
                                            NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                        }

                                    }
                                }
                            }
                            TotalBlockWrong++;
                            ConsoleLog.WriteLine("Block not accepted, stop mining, wait new block.", ClassConsoleColorEnumeration.IndexConsoleRedLog);
                            break;
                        case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareAleady:
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                    {
                                        if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendJobStatus + ClassConnectorSetting.PacketContentSeperator + ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareBad).ConfigureAwait(false))
                                        {
                                            NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                        }

                                    }
                                }
                            }
                            ConsoleLog.WriteLine("Block already mined, stop mining, wait new block.", ClassConsoleColorEnumeration.IndexConsoleRedLog);
                            break;
                        case ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareNotExist:
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                    {
                                        if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendJobStatus + ClassConnectorSetting.PacketContentSeperator + ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.ShareNotExist).ConfigureAwait(false))
                                        {
                                            NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                        }

                                    }
                                }
                            }
                            ConsoleLog.WriteLine("Block mined not exist, stop mining, wait new block.", ClassConsoleColorEnumeration.IndexConsoleBlueLog);
                            break;

                    }
                    break;
            }
            return true;
        }

        /// <summary>
        /// Spread job range to miners.
        /// </summary>
        /// <param name="minerId"></param>
        public static async void SpreadJobAsync(int minerId = -1)
        {
            try
            {
                var splitBlockContent = Blocktemplate.Split(new[] { "&" }, StringSplitOptions.None);

                CurrentBlockId = splitBlockContent[0].Replace("ID=", "");
                if (FirstBlockId == 0)
                {
                    int.TryParse(CurrentBlockId, out FirstBlockId);
                }
                if (CurrentBlockId != "" && CurrentBlockId.Length > 0)
                {
                    CurrentBlockHash = splitBlockContent[1].Replace("HASH=", "");
                    CurrentBlockAlgorithm = splitBlockContent[2].Replace("ALGORITHM=", "");
                    CurrentBlockSize = splitBlockContent[3].Replace("SIZE=", "");
                    CurrentBlockMethod = splitBlockContent[4].Replace("METHOD=", "");
                    CurrentBlockKey = splitBlockContent[5].Replace("KEY=", "");
                    CurrentBlockJob = splitBlockContent[6].Replace("JOB=", "");
                    CurrentBlockReward = splitBlockContent[7].Replace("REWARD=", "");
                    CurrentBlockDifficulty = splitBlockContent[8].Replace("DIFFICULTY=", "");
                    CurrentBlockTimestampCreate = splitBlockContent[9].Replace("TIMESTAMP=", "");
                    CurrentBlockIndication = splitBlockContent[10].Replace("INDICATION=", "");
                    CurrentBlockNetworkHashrate = splitBlockContent[11].Replace("NETWORK_HASHRATE=", "");
                    CurrentBlockLifetime = splitBlockContent[12].Replace("LIFETIME=", "");

                    var splitCurrentBlockJob = CurrentBlockJob.Split(new[] { ";" }, StringSplitOptions.None);
                    var minRange = decimal.Parse(splitCurrentBlockJob[0]);
                    var maxRange = decimal.Parse(splitCurrentBlockJob[1]);

                    if (ListMinerStats != null)
                    {
                        int totalMinerConnected = 0;
                        foreach(var minerStats in ListMinerStats)
                        {
                            if (minerStats.Value.MinerConnectionStatus)
                            {
                                totalMinerConnected++;
                            }
                        }
                        int i1 = 0;

                        if (minerId != -1)
                        {
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    try
                                    {
                                        if (NetworkProxy.ListOfMiners[i] != null)
                                        {
                                            if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                            {
                                                if (NetworkProxy.ListOfMiners[i].MinerInitialized)
                                                {
                                                    if (ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyStart <= 0 && ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyEnd <= 0)
                                                    {
                                                        i1++;


                                                        if (NetworkProxy.ListOfMiners[i].MinerId == minerId)
                                                        {
                                                            var minRangeTmp = (decimal)Math.Round((maxRange / totalMinerConnected) * (i1 - 1), 0);
                                                            var maxRangeTmp = (decimal)(Math.Round(((maxRange / totalMinerConnected) * i1), 0));


                                                            if (minRangeTmp < minRange)
                                                            {
                                                                minRangeTmp = minRange;
                                                            }

                                                            var blocktemplateTmp = "ID=" + CurrentBlockId
                                                                                         + "&HASH=" + CurrentBlockHash
                                                                                         + "&ALGORITHM=" +
                                                                                         CurrentBlockAlgorithm
                                                                                         + "&SIZE=" + CurrentBlockSize
                                                                                         + "&METHOD=" +
                                                                                         CurrentBlockMethod
                                                                                         + "&KEY=" + CurrentBlockKey
                                                                                         + "&JOB=" + minRangeTmp
                                                                                         + ";" + maxRangeTmp
                                                                                         + "&REWARD=" +
                                                                                         CurrentBlockReward
                                                                                         + "&DIFFICULTY=" +
                                                                                         CurrentBlockDifficulty
                                                                                         + "&TIMESTAMP=" +
                                                                                         CurrentBlockTimestampCreate
                                                                                         + "&INDICATION=" +
                                                                                         CurrentBlockIndication
                                                                                         + "&NETWORK_HASHRATE=" +
                                                                                         CurrentBlockNetworkHashrate
                                                                                         + "&LIFETIME=" +
                                                                                         CurrentBlockLifetime;

                                                            //ConsoleLog.WriteLine("Send job: " + minRangeTmp + "/" + maxRangeTmp + " range to miner: " + NetworkProxy.ListOfMiners[i].MinerName, ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                                            if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendCurrentBlockMining + ClassConnectorSetting.PacketContentSeperator + blocktemplateTmp).ConfigureAwait(false))
                                                            {
                                                                NetworkProxy.ListOfMiners[i].MinerInitialized = false;
                                                                NetworkProxy.ListOfMiners[i].MinerConnected = false;
                                                                try
                                                                {
                                                                    NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                                                }
                                                                catch
                                                                {

                                                                }
                                                                NetworkProxy.ListOfMiners[i] = null;
                                                            }
                                                            else
                                                            {
                                                                ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerLastBlockTemplateReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (NetworkProxy.ListOfMiners[i].MinerId == minerId)
                                                        {
                                                            //ConsoleLog.WriteLine(NetworkProxy.ListOfMiners[i].MinerName + " select start position range: " + ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyStart + " | select end position range: " + ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyEnd, ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                                            var minerJobRangePosition = ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyStart;
                                                            var minerJobRangePourcentage = ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyEnd;

                                                            if (minerJobRangePourcentage <= 0)
                                                            {
                                                                minerJobRangePourcentage = 100;
                                                            }
                                                            if (minerJobRangePosition > 100)
                                                            {
                                                                minerJobRangePosition = 100;
                                                            }


                                                            var minerJobRangePositionStart = (maxRange * minerJobRangePosition) / 100;
                                                            var minerJobRangePositionEnd = (maxRange * minerJobRangePourcentage) / 100;
                                                            if (minerJobRangePositionEnd <= minerJobRangePositionStart)
                                                            {
                                                                minerJobRangePositionEnd = minerJobRangePositionEnd + minerJobRangePositionStart;
                                                            }
                                                            var minRangeTmp = (decimal)Math.Round(minerJobRangePositionStart, 0);
                                                            var maxRangeTmp = (decimal)Math.Round(minerJobRangePositionEnd, 0);

                                                            if (minRangeTmp < minRange)
                                                            {
                                                                minRangeTmp = minRange;
                                                            }



                                                            var blocktemplateTmp = "ID=" + CurrentBlockId
                                                                                         + "&HASH=" + CurrentBlockHash
                                                                                         + "&ALGORITHM=" +
                                                                                         CurrentBlockAlgorithm
                                                                                         + "&SIZE=" + CurrentBlockSize
                                                                                         + "&METHOD=" +
                                                                                         CurrentBlockMethod
                                                                                         + "&KEY=" + CurrentBlockKey
                                                                                         + "&JOB=" + minRangeTmp
                                                                                         + ";" + maxRangeTmp
                                                                                         + "&REWARD=" +
                                                                                         CurrentBlockReward
                                                                                         + "&DIFFICULTY=" +
                                                                                         CurrentBlockDifficulty
                                                                                         + "&TIMESTAMP=" +
                                                                                         CurrentBlockTimestampCreate
                                                                                         + "&INDICATION=" +
                                                                                         CurrentBlockIndication
                                                                                         + "&NETWORK_HASHRATE=" +
                                                                                         CurrentBlockNetworkHashrate
                                                                                         + "&LIFETIME=" +
                                                                                         CurrentBlockLifetime;

                                                            //ConsoleLog.WriteLine("Send job: " + minRangeTmp + "/" + maxRangeTmp + " range to miner: " + NetworkProxy.ListOfMiners[i].MinerName, ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                                            if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendCurrentBlockMining + ClassConnectorSetting.PacketContentSeperator + blocktemplateTmp).ConfigureAwait(false))
                                                            {
                                                                NetworkProxy.ListOfMiners[i].MinerInitialized = false;
                                                                NetworkProxy.ListOfMiners[i].MinerConnected = false;
                                                                try
                                                                {
                                                                    NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                                                }
                                                                catch
                                                                {

                                                                }
                                                                NetworkProxy.ListOfMiners[i] = null;
                                                            }
                                                            else
                                                            {
                                                                ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerLastBlockTemplateReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                                                            }
                                                        }
                                                    }
                                                }

                                            }
                                        }
                                    }
                                    catch
                                    {

                                    }
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                            {
                                if (i < NetworkProxy.ListOfMiners.Count)
                                {
                                    try
                                    {
                                        if (NetworkProxy.ListOfMiners[i] != null)
                                        {
                                            if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                            {
                                                if (NetworkProxy.ListOfMiners[i].MinerInitialized)
                                                {
                                                    if (ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyStart <= 0 && ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyEnd <= 0)
                                                    {
                                                        i1++;


                                                        var minRangeTmp = (decimal)Math.Round((maxRange / totalMinerConnected) * (i1 - 1), 0);
                                                        var maxRangeTmp = (decimal)(Math.Round(((maxRange / totalMinerConnected) * i1), 0));


                                                        if (minRangeTmp < minRange)
                                                        {
                                                            minRangeTmp = minRange;
                                                        }


                                                        var blocktemplateTmp = "ID=" + CurrentBlockId
                                                                                     + "&HASH=" + CurrentBlockHash
                                                                                     + "&ALGORITHM=" +
                                                                                     CurrentBlockAlgorithm
                                                                                     + "&SIZE=" + CurrentBlockSize
                                                                                     + "&METHOD=" +
                                                                                     CurrentBlockMethod
                                                                                     + "&KEY=" + CurrentBlockKey
                                                                                     + "&JOB=" + minRangeTmp
                                                                                     + ";" + maxRangeTmp
                                                                                     + "&REWARD=" +
                                                                                     CurrentBlockReward
                                                                                     + "&DIFFICULTY=" +
                                                                                     CurrentBlockDifficulty
                                                                                     + "&TIMESTAMP=" +
                                                                                     CurrentBlockTimestampCreate
                                                                                     + "&INDICATION=" +
                                                                                     CurrentBlockIndication
                                                                                     + "&NETWORK_HASHRATE=" +
                                                                                     CurrentBlockNetworkHashrate
                                                                                     + "&LIFETIME=" +
                                                                                     CurrentBlockLifetime;

                                                        //ConsoleLog.WriteLine("Send job: " + minRangeTmp + "/" + maxRangeTmp + " range to miner: " + NetworkProxy.ListOfMiners[i].MinerName, ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                                        if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendCurrentBlockMining + ClassConnectorSetting.PacketContentSeperator + blocktemplateTmp).ConfigureAwait(false))
                                                        {
                                                            NetworkProxy.ListOfMiners[i].MinerInitialized = false;
                                                            NetworkProxy.ListOfMiners[i].MinerConnected = false;
                                                            try
                                                            {
                                                                NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                                            }
                                                            catch
                                                            {

                                                            }
                                                            NetworkProxy.ListOfMiners[i] = null;
                                                        }
                                                        else
                                                        {
                                                            ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerLastBlockTemplateReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                                                        }

                                                    }
                                                    else
                                                    {
                                                        //ConsoleLog.WriteLine(NetworkProxy.ListOfMiners[i].MinerName + " select start position range: " + ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyStart + " | select end position range: " + ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyEnd, ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                                        var minerJobRangePosition = ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyStart;
                                                        var minerJobRangePourcentage = ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerDifficultyEnd;

                                                        if (minerJobRangePourcentage <= 0)
                                                        {
                                                            minerJobRangePourcentage = 100;
                                                        }
                                                        if (minerJobRangePosition > 100)
                                                        {
                                                            minerJobRangePosition = 100;
                                                        }


                                                        var minerJobRangePositionStart = (maxRange * minerJobRangePosition) / 100;
                                                        var minerJobRangePositionEnd = (maxRange * minerJobRangePourcentage) / 100;
                                                        if (minerJobRangePositionEnd <= minerJobRangePositionStart)
                                                        {
                                                            minerJobRangePositionEnd = minerJobRangePositionEnd + minerJobRangePositionStart;
                                                        }
                                                        var minRangeTmp = (decimal)Math.Round(minerJobRangePositionStart, 0);
                                                        var maxRangeTmp = (decimal)Math.Round(minerJobRangePositionEnd, 0);


                                                        if (minRangeTmp < minRange)
                                                        {
                                                            minRangeTmp = minRange;
                                                        }




                                                        var blocktemplateTmp = "ID=" + CurrentBlockId
                                                                                     + "&HASH=" + CurrentBlockHash
                                                                                     + "&ALGORITHM=" +
                                                                                     CurrentBlockAlgorithm
                                                                                     + "&SIZE=" + CurrentBlockSize
                                                                                     + "&METHOD=" +
                                                                                     CurrentBlockMethod
                                                                                     + "&KEY=" + CurrentBlockKey
                                                                                     + "&JOB=" + minRangeTmp
                                                                                     + ";" + maxRangeTmp
                                                                                     + "&REWARD=" +
                                                                                     CurrentBlockReward
                                                                                     + "&DIFFICULTY=" +
                                                                                     CurrentBlockDifficulty
                                                                                     + "&TIMESTAMP=" +
                                                                                     CurrentBlockTimestampCreate
                                                                                     + "&INDICATION=" +
                                                                                     CurrentBlockIndication
                                                                                     + "&NETWORK_HASHRATE=" +
                                                                                     CurrentBlockNetworkHashrate
                                                                                     + "&LIFETIME=" +
                                                                                     CurrentBlockLifetime;

                                                        //ConsoleLog.WriteLine("Send job: " + minRangeTmp + "/" + maxRangeTmp + " range to miner: " + NetworkProxy.ListOfMiners[i].MinerName, ClassConsoleColorEnumeration.IndexConsoleYellowLog);
                                                        if (!await NetworkProxy.ListOfMiners[i].SendPacketAsync(ClassSoloMiningPacketEnumeration.SoloMiningRecvPacketEnumeration.SendCurrentBlockMining + ClassConnectorSetting.PacketContentSeperator + blocktemplateTmp).ConfigureAwait(false))
                                                        {
                                                            NetworkProxy.ListOfMiners[i].MinerInitialized = false;
                                                            NetworkProxy.ListOfMiners[i].MinerConnected = false;
                                                            try
                                                            {
                                                                NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                                            }
                                                            catch
                                                            {

                                                            }
                                                            NetworkProxy.ListOfMiners[i] = null;
                                                        }
                                                        else
                                                        {
                                                            ListMinerStats[NetworkProxy.ListOfMiners[i].MinerName].MinerLastBlockTemplateReceived = DateTimeOffset.Now.ToUnixTimeSeconds();
                                                        }
                                                    }
                                                }

                                            }
                                        }
                                    }
                                    catch
                                    {

                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                FastWriteExceptionError(error);
                if (NetworkProxy.ListOfMiners.Count > 0)
                {
                    for (int i = 0; i < NetworkProxy.ListOfMiners.Count; i++)
                    {
                        if (i < NetworkProxy.ListOfMiners.Count)
                        {
                            if (NetworkProxy.ListOfMiners[i] != null)
                            {
                                try
                                {
                                    if (NetworkProxy.ListOfMiners[i].MinerConnected)
                                    {
                                        NetworkProxy.ListOfMiners[i].DisconnectMiner();
                                    }
                                }
                                catch
                                {
                                    
                                }
                            }
                        }
                    }
                }
            }
            
        }

        /// <summary>
        /// Method for ask and update mining methods.
        /// </summary>
        private static void AskMiningMethod()
        {
            try
            {
                Task.Factory.StartNew(async delegate()
                {
                    while (IsConnected)
                    {
                        if (!await _classSeedNodeConnector
                            .SendPacketToSeedNodeAsync(
                                ClassSoloMiningPacketEnumeration.SoloMiningSendPacketEnumeration
                                    .ReceiveAskListBlockMethod + ClassConnectorSetting.PacketMiningSplitSeperator, Program.NetworkCertificate, false, true)
                            .ConfigureAwait(false))
                        {
                            IsConnected = false;
                            break;
                        }

                        while (ListOfMiningMethodContent.Count == 0)
                        {
                            await Task.Delay(100);
                        }

                        if (!await _classSeedNodeConnector
                            .SendPacketToSeedNodeAsync(
                                ClassSoloMiningPacketEnumeration.SoloMiningSendPacketEnumeration
                                    .ReceiveAskCurrentBlockMining + ClassConnectorSetting.PacketMiningSplitSeperator, Program.NetworkCertificate, false, true)
                            .ConfigureAwait(false))
                        {
                            IsConnected = false;
                            break;
                        }

                        await Task.Delay(1000);
                    }
                }, _cancellationTokenListenNetwork.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
            }
            catch
            {

            }
        }

        /// <summary>
        /// Send packet to the network.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="encrypted"></param>
        /// <returns></returns>
        public static async Task<bool> SendPacketAsync(string packet, bool encrypted)
        {
            if (encrypted)
            {
                if (!await _classSeedNodeConnector.SendPacketToSeedNodeAsync(packet, Program.NetworkCertificate, false, encrypted).ConfigureAwait(false))
                {
                    return false;
                }
            }
            else
            {
                if (!await _classSeedNodeConnector.SendPacketToSeedNodeAsync(packet, string.Empty, false, encrypted).ConfigureAwait(false))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Saved fast exception error.
        /// </summary>
        /// <param name="error"></param>
        public static void FastWriteExceptionError(Exception error)
        {
            var filePath = Program.ConvertPath(System.AppDomain.CurrentDomain.BaseDirectory + "\\fast_error_proxy_miner.txt");
            var exception = error;
            using (var writer = new StreamWriter(filePath) { AutoFlush = true })
            {
                writer.WriteLine("Message :" + exception.Message + "<br/>" + Environment.NewLine +
                                 "StackTrace :" +
                                 exception.StackTrace +
                                 "" + Environment.NewLine + "Date :" + DateTime.Now);
                writer.WriteLine(Environment.NewLine +
                                 "-----------------------------------------------------------------------------" +
                                 Environment.NewLine);
            }

        }
    }
}
