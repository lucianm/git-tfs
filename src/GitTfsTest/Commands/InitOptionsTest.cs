using GitTfs.Commands;
using Moq.AutoMock;
using NDesk.Options;
using Xunit;

namespace GitTfs.Test.Commands
{
    public class InitOptionsTest : BaseTest
    {
        private readonly AutoMocker mocks;
        private readonly InitOptions classUnderTest;

        public InitOptionsTest()
        {
            mocks = new AutoMocker();
            classUnderTest = mocks.CreateInstance<InitOptions>();
        }

        #region autocrlf option tests

        [Fact]
        public void AutoCrlfDefault() => Assert.Equal("false", classUnderTest.GitInitAutoCrlf);

        [Fact]
        public void AutoCrlfProvideTrue()
        {
            string[] args = { "init", "--autocrlf=true", "http://example.com/tfs", "$/Junk" };
            classUnderTest.OptionSet.Parse(args);
            Assert.Equal("true", classUnderTest.GitInitAutoCrlf);
        }

        [Fact]
        public void AutoCrlfProvideFalse()
        {
            string[] args = { "init", "--autocrlf=false", "http://example.com/tfs", "$/Junk" };
            classUnderTest.OptionSet.Parse(args);
            Assert.Equal("false", classUnderTest.GitInitAutoCrlf);
        }

        [Fact]
        public void AutoCrlfProvideAuto()
        {
            string[] args = { "init", "--autocrlf=auto", "http://example.com/tfs", "$/Junk" };
            classUnderTest.OptionSet.Parse(args);
            Assert.Equal("auto", classUnderTest.GitInitAutoCrlf);
        }

        [Fact]
        public void AutoCrlfProvideInvalidOption()
        {
            string[] args = { "init", "--autocrlf=windows", "http://example.com/tfs", "$/Junk" };
            Assert.Throws<OptionException>(() => classUnderTest.OptionSet.Parse(args));
            Assert.Equal("false", classUnderTest.GitInitAutoCrlf);
        }

        [Fact]
        public void AutoCrlfProvidedNoArg()
        {
            string[] args = { "init", "--autocrlf", "http://example.com/tfs", "$/Junk" };
            Assert.Throws<OptionException>(() => classUnderTest.OptionSet.Parse(args));
            Assert.Equal("false", classUnderTest.GitInitAutoCrlf);
        }

        #endregion

        #region ignorecase option tests

        [Fact]
        public void IgnorecaseDefault() =>
            // depends on global setting..
            Assert.Null(classUnderTest.GitInitIgnoreCase);

        [Fact]
        public void IgnoreCaseProvideTrue()
        {
            string[] args = { "init", "--ignorecase=true", "http://example.com/tfs", "$/Junk" };
            classUnderTest.OptionSet.Parse(args);
            Assert.Equal("true", classUnderTest.GitInitIgnoreCase);
        }

        [Fact]
        public void IgnoreCaseProvideFalse()
        {
            string[] args = { "init", "--ignorecase=false", "http://example.com/tfs", "$/Junk" };
            classUnderTest.OptionSet.Parse(args);
            Assert.Equal("false", classUnderTest.GitInitIgnoreCase);
        }

        [Fact]
        public void IgnoreCaseProvideInvalidOption()
        {
            string[] args = { "init", "--ignorecase=windows", "http://example.com/tfs", "$/Junk" };
            Assert.Throws<OptionException>(() => classUnderTest.OptionSet.Parse(args));
            Assert.Null(classUnderTest.GitInitIgnoreCase);
        }

        [Fact]
        public void IgnoreCaseProvideNoArg()
        {
            string[] args = { "init", "--ignorecase", "http://example.com/tfs", "$/Junk" };
            Assert.Throws<OptionException>(() => classUnderTest.OptionSet.Parse(args));
            Assert.Null(classUnderTest.GitInitIgnoreCase);
        }

        #endregion
    }
}
