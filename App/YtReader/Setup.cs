﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SysExtensions.Collections;
using SysExtensions.Fluent.IO;
using SysExtensions.IO;
using SysExtensions.Serialization;
using SysExtensions.Text;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ContainerInstance.Fluent;
using Microsoft.Azure.Management.ContainerInstance.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using SysExtensions.Security;
using Serilog.Sinks.Debug;

namespace YtReader {
    public static class Setup {
        public static string AppName = "YouTubeNetworks";
        public static FPath SolutionDir => typeof(Setup).LocalAssemblyPath().ParentWithFile("YouTubeNetworks.sln");
        public static FPath SolutionDataDir => typeof(Setup).LocalAssemblyPath().DirOfParent("Data");
        public static FPath LocalDataDir => "Data".AsPath().InAppData(AppName);

        public static Logger CreateTestLogger() => new LoggerConfiguration()
            .WriteTo.Seq("http://localhost:5341", LogEventLevel.Verbose)
            .CreateLogger();

        public static Logger CreateCliLogger(AppCfg cfg = null) {
            var c = new LoggerConfiguration()
                .WriteTo.Console();

            if (cfg != null)
                c.WriteTo.ApplicationInsightsTraces(cfg.AppInsightsKey);

            return c.CreateLogger();
        }

        static FPath RootCfgPath => "cfg.json".AsPath().InAppData(AppName);

        static string GetEnv(string name) => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);

        public static async Task<Cfg> LoadCfg(ILogger log = null) {
            var rootCfg = new RootCfg();
            rootCfg.AzureStorageCs = GetEnv("YtNetworks_AzureStorageCs");

            if (rootCfg.AzureStorageCs.NullOrEmpty()) throw new InvalidOperationException("AzureStorageCs variable not provided");



            var storageAccount = CloudStorageAccount.Parse(rootCfg.AzureStorageCs);
            var cloudBlobClient = storageAccount.CreateCloudBlobClient();
            var cfg = (await cloudBlobClient.GetText("cfg", $"{rootCfg.Environment}.json")).ToObject<AppCfg>();

            return new Cfg { App = cfg, Root = rootCfg };
        }
    }

    public static class ChannelConfigExtensions {
        public static async Task<ChannelConfig> LoadChannelConfig(this AppCfg cfg) {
            var channelCfg = new ChannelConfig();
            var csv = await new WebClient().DownloadStringTaskAsync(cfg.YtReader.SeedsUrl);
            var seedData = CsvExtensions.ReadFromCsv<SeedChannel>(csv);
            channelCfg.Seeds.AddRange(cfg.LimitedToSeedChannels != null ? seedData.Where(s => cfg.LimitedToSeedChannels.Contains(s.Id)) : seedData);
            //channelCfg.Excluded.AddRange(cfg.CrawlConfigDir.Combine("ChannelExclude.csv").ReadFromCsv<InfluencerOverride>());
            return channelCfg;
        }

        public static ISimpleFileStore DataStore(this Cfg cfg, StringPath path = null) =>
            new AzureBlobFileStore(cfg.App.Storage.DataStorageCs, path ?? cfg.App.Storage.DbPath);

        public static YtStore YtStore(this Cfg cfg, ILogger log) {
            var reader = new YtClient(cfg.App, log);
            var ytStore = new YtStore(reader, cfg.DataStore(cfg.App.Storage.DbPath));
            return ytStore;
        }

        //static IEnumerable<SeedChannel> SeedChannels(this Cfg cfg) => cfg.CrawlConfigDir.Combine("SeedChannels.csv").ReadFromCsv<SeedChannel>();
    }


    public class RootCfg {
        // connection string to the configuration directory
        public string AzureStorageCs { get; set; }

        // name of environment (Prod/Dev/MarkDev etc..). used to choose appropreate cfg
        public string Environment { get; set; } = "Prod";
    }

    public class Cfg {
        public AppCfg App { get; set; }
        public RootCfg Root { get; set; }
    }

    public class AppCfg {
        public string AppInsightsKey { get; set; }
        public int Parallel { get; set; } = 8;
        public int ParallelCollect {get; set;} = 24;

        public string ResourceGroup { get; set; } = "ytnetworks";
        public YtReaderCfg YtReader { get; set; } = new YtReaderCfg();
        public StorageCfg Storage { get; set; } = new StorageCfg();
        public ICollection<string> YTApiKeys { get; set; }
        public ICollection<string> LimitedToSeedChannels { get; set; }
        public CollectionCacheType CacheType { get; set; } = CollectionCacheType.Memory;
        public string SubscriptionId { get; set; }
        public ServicePrincipalCfg ServicePrincipal { get; set; } = new ServicePrincipalCfg();
        public ContainerCfg Container { get; set; } = new ContainerCfg();
    }

    public class YtReaderCfg {
        public int CacheRelated = 40;
        public int Related { get; set; } = 10;
        public DateTime From { get; set; }
        public DateTime? To { get; set; }


        public TimeSpan VideoDead { get; set; } = 365.Days();
        public TimeSpan VideoOld { get; set; } = 30.Days();
        public TimeSpan RefreshOldVideos { get; set; } = 7.Days();
        public TimeSpan RefreshYoungVideos { get; set; } = 24.Hours();
        public TimeSpan RefreshChannel { get; set; } = 7.Days();
        public TimeSpan RefreshRelatedVideos { get; set; } = 30.Days();
        public TimeSpan RefreshChannelVideos { get; set; } = 24.Hours();

        public Uri SeedsUrl { get; set; } = new Uri("https://raw.githubusercontent.com/markledwich2/YouTubeNetworks/master/Data/SeedChannels.csv");
    }

    public class StorageCfg {
        public string DataStorageCs { get; set; }
        public string DbPath { get; set; } = "data/db";
        public string AnalysisPath { get; set; } = "data/analysis";
    }

    public class ContainerCfg {
        public string Registry { get; set; } = "ytnetworks.azurecr.io";
        public string Name { get; set; } = "ytnetworks-auto";
        public string ImageName { get; set; } = "ytnetworks";
        public int Cores {get;set;} = 2;
        public double Mem { get;set;} = 5;
        public NameSecret RegistryCreds { get; set; }
    }

    public class ServicePrincipalCfg {
        public string ClientId { get; set; }
        public string Secret { get; set; }
        public string TennantId { get; set; }
    }

    public class BatchCfg {
        public string Url { get; set; }
        public string Key { get; set; }
        public string Account { get; set; }
        public string Pool { get; set; } = "win";

    }

    public class SeedChannel {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string LR { get; set; }
    }

    public class InfluencerOverride {
        public string Id { get; set; }
        public string Title { get; set; }
    }

    public class ChannelConfig {
        public IKeyedCollection<string, SeedChannel> Seeds { get; } = new KeyedCollection<string, SeedChannel>(c => c.Id);
        public IKeyedCollection<string, InfluencerOverride> Excluded { get; } = new KeyedCollection<string, InfluencerOverride>(c => c.Id);
    }


}