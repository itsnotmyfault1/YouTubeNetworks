using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YtReader;

namespace YouTubeReaderTests {
    [TestClass]
    public class YTReaderTests {
        [TestMethod]
        public async Task SaveChannelRelationData() {
            var cfg = await Setup.LoadCfg();
            var log = Setup.CreateTestLogger();

            log.Information("started test");

            var store = new YtStore(new YtClient(cfg.App, log), cfg.DataStore());
            var analysis = new YtCollect(store, cfg.DataStore(cfg.App.Storage.AnalysisPath), cfg.App, log);
            await analysis.SaveChannelRelationData();
        }


        [TestMethod]
        public async Task SaveCaptions() {
            var cfg = await Setup.LoadCfg();
            var log = Setup.CreateTestLogger();

            var videoIds = new[] { "P0dcyAXXmCw" };
            var store = new YtStore(new YtClient(cfg.App, log), cfg.DataStore());

            foreach (var videoId in videoIds) {
                var txt = await store.GetAndUpdateVideoCaptions(videoId);
            }
        }
    }
}