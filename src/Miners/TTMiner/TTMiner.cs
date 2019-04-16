﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MinerPlugin;
using MinerPluginToolkitV1;
using MinerPluginToolkitV1.Interfaces;
using MinerPluginToolkitV1.ExtraLaunchParameters;
using Newtonsoft.Json;
using NiceHashMinerLegacy.Common;
using NiceHashMinerLegacy.Common.Device;
using NiceHashMinerLegacy.Common.Enums;
using MinerPluginToolkitV1.ClaymoreCommon;

namespace TTMiner
{
    // TODO get ordered API data
    public class TTMiner : MinerBase, IDisposable, IAfterStartMining
    {
        private int _apiPort;
        private readonly string _uuid;
        private AlgorithmType _algorithmType;
        protected List<MiningPair> _orderedMiningPairs = new List<MiningPair>();

        private string _devices;
        private string _extraLaunchParameters = "";

        // TODO figure out how to fix API workaround without this started time
        private DateTime _started;

        private class JsonApiResponse
        {
#pragma warning disable IDE1006 // Naming Styles
            public List<string> result { get; set; }
            public int id { get; set; }
            public object error { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        }

        private string AlgoName
        {
            get
            {
                switch (_algorithmType)
                {
                    case AlgorithmType.MTP:
                        return "mtp";
                    case AlgorithmType.Lyra2REv3:
                        return "LYRA2V3";
                    default:
                        return "";
                }
            }
        }

        private double DevFee
        {
            get
            {
                return 0.01d; // 1% for all
            }
        }

        public TTMiner(string uuid)
        {
            _uuid = uuid;
        }

        public override async Task<BenchmarkResult> StartBenchmark(CancellationToken stop, BenchmarkPerformanceType benchmarkType = BenchmarkPerformanceType.Standard)
        {
            int benchTime;
            switch (benchmarkType)
            {
                case BenchmarkPerformanceType.Quick:
                    benchTime = 20;
                    break;
                case BenchmarkPerformanceType.Precise:
                    benchTime = 120;
                    break;
                default:
                    benchTime = 60;
                    break;
            }

            var commandLine = CreateCommandLine(MinerToolkit.DemoUserBTC);
            var binPathBinCwdPair = GetBinAndCwdPaths();
            var binPath = binPathBinCwdPair.Item1;
            var binCwd = binPathBinCwdPair.Item2;
            var bp = new BenchmarkProcess(binPath, binCwd, commandLine, GetEnvironmentVariables());

            var benchHashes = 0d;
            var benchIters = 0;
            var benchHashResult = 0d;
            var targetBenchIters = 2; //Math.Max(1, (int)Math.Floor(benchTime / 20d));

            bp.CheckData = (data) =>
            {
                var hashrateFoundPair = data.ToLower().TryGetHashrateAfter("]:");
                var hashrate = hashrateFoundPair.Item1;
                var found = hashrateFoundPair.Item2;

                if (data.Contains("LastShare") && data.Contains("GPU[") && found && hashrate > 0)
                {
                    benchHashes += hashrate;
                    benchIters++;
                    benchHashResult = (benchHashes / benchIters) * (1.0d - DevFee);
                }
                return new BenchmarkResult
                {
                    AlgorithmTypeSpeeds = new List<AlgorithmTypeSpeedPair> { new AlgorithmTypeSpeedPair(_algorithmType, benchHashResult) },
                    Success = benchIters >= targetBenchIters
                };
            };

            var timeout = TimeSpan.FromSeconds(benchTime + 5);
            var benchWait = TimeSpan.FromMilliseconds(500);
            var t = MinerToolkit.WaitBenchmarkResult(bp, timeout, benchWait, stop);
            return await t;
        }

        protected override Tuple<string, string> GetBinAndCwdPaths()
        {
            var pluginRoot = Path.Combine(Paths.MinerPluginsPath(), _uuid);
            var pluginRootBins = Path.Combine(pluginRoot, "bins");
            var binPath = Path.Combine(pluginRootBins, "TT-Miner.exe");
            var binCwd = pluginRootBins;
            return Tuple.Create(binPath, binCwd);
        }

        protected override string MiningCreateCommandLine()
        {
            return CreateCommandLine(_username);
        }

        private string CreateCommandLine(string username)
        {
            _apiPort = MinersApiPortsManager.GetAvaliablePortInRange();
            var url = StratumServiceHelpers.GetLocationUrl(_algorithmType, _miningLocation, NhmConectionType.STRATUM_TCP);
            var cmd = $"-a {AlgoName} -url {url} -u {username} -d {_devices} --api-bind 127.0.0.1:{_apiPort} {_extraLaunchParameters}";
            return cmd;
        }

        public async override Task<ApiData> GetMinerStatsDataAsync()
        {
            var api = new ApiData();
            var elapsedSeconds = DateTime.Now.Subtract(_started).Seconds;
            if (elapsedSeconds < 15)
            {
                return api;
            }

            var miningDevices = _orderedMiningPairs.Select(pair => pair.Device).ToList();
            var algorithmTypes = new AlgorithmType[] { _algorithmType };
            return await ClaymoreAPIHelpers.GetMinerStatsDataAsync(_apiPort, miningDevices, algorithmTypes);
        }

        protected override void Init()
        {
            var singleType = MinerToolkit.GetAlgorithmSingleType(_miningPairs);
            _algorithmType = singleType.Item1;
            bool ok = singleType.Item2;
            if (!ok) throw new InvalidOperationException("Invalid mining initialization");

            // Order pairs and parse ELP
            _orderedMiningPairs = _miningPairs.ToList();
            _orderedMiningPairs.Sort((a, b) => a.Device.ID.CompareTo(b.Device.ID));
            _devices = string.Join(" ", _orderedMiningPairs.Select(p => p.Device.ID));
            if (MinerOptionsPackage != null)
            {
                // TODO add ignore temperature checks
                var generalParams = Parser.Parse(_orderedMiningPairs, MinerOptionsPackage.GeneralOptions);
                var temperatureParams = Parser.Parse(_orderedMiningPairs, MinerOptionsPackage.TemperatureOptions);
                _extraLaunchParameters = $"{generalParams} {temperatureParams}".Trim();
            }
        }

        public void AfterStartMining()
        {
            _started = DateTime.Now;
        }

        public void Dispose()
        {
        }
    }
}