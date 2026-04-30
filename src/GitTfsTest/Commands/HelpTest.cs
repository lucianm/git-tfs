using GitTfs.Commands;
using GitTfs.Util;
using Moq.AutoMock;
using StructureMap;
using NDesk.Options;
using Xunit;
using NLog;
using System.Diagnostics;
using NLog.Config;
using NLog.Targets;

namespace GitTfs.Test.Commands
{
    public class HelpTest : BaseTest
    {
        private readonly AutoMocker mocks;
        private readonly Help helpCommand;

        public HelpTest()
        {
            mocks = new AutoMocker();
            var container = new Container(cfg =>
            {
                cfg.For<GitTfsCommand>().Add<TestCommand>().Named("test");
            });
            mocks.Use<IContainer>(container);
            helpCommand = mocks.CreateInstance<Help>();
        }

        public MemoryTarget GetTestLogger()
        {
            var memoryTarget = new MemoryTarget() { Layout = @"${message}" };

            var config = new LoggingConfiguration();
            config.AddTarget("memory", memoryTarget);
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, memoryTarget));

            LogManager.Configuration = config;

            Trace.Listeners.Add(new NLogTraceListener());

            return memoryTarget;
        }

        [Fact]
        public void ShouldWriteGeneralHelp()
        {
            var memoryTarget = GetTestLogger();

            helpCommand.Run();

            memoryTarget.Logs[0].Equals("Usage: git-tfs [command] [options]");
            memoryTarget.Logs[1].Contains("test");
            memoryTarget.Logs[2].Equals(" (use 'git-tfs help [command]' or 'git-tfs [command] --help' for more information)");
            memoryTarget.Logs[3].Equals("Find more help in our online help : https://github.com/git-tfs/git-tfs");
        }

        [Fact]
        public void ShouldWriteCommandHelp()
        {
            var memoryTarget = GetTestLogger();
            helpCommand.Run(new[] { "test" });

            memoryTarget.Logs[0].Equals("Usage: git-tfs test [options]");
        }

        public class TestCommand : GitTfsCommand
        {
            public bool Flag { get; set; }

            private readonly OptionSet TestOptions = new OptionSet();

            public OptionSet OptionSet => TestOptions;

            public int Run(IList<string> args) => throw new System.NotImplementedException();
        }
    }
}
