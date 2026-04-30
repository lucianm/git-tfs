using GitTfs.Core.TfsInterop;
using GitTfs.Test;
using GitTfs.VsCommon;
using Moq;
using StructureMap;
using Xunit;

namespace GitTfsTest.Core
{
    public class TfsApiBridgeTest : BaseTest
    {
        private readonly TfsApiBridge _bridge;

        public TfsApiBridgeTest()
        {
            // If TfsApiBridge needs a container, create a minimal one
            var container = new Container();
            _bridge = new TfsApiBridge(container);
        }

        [Fact]
        public void ConvertsEnum() =>
            Assert.Equal(OriginalEnum.Value2, _bridge.Convert<OriginalEnum>(WrappedEnum.Value2));

        [Fact]
        public void WrapsAndUnwrapsObject()
        {
            var originalObject = new OriginalType();
            var wrappedObject = _bridge.Wrap<WrapperForOriginalType, OriginalType>(originalObject);
            Assert.Equal(originalObject, _bridge.Unwrap<OriginalType>(wrappedObject));
        }

        [Fact]
        public void WrapsObjectWithBridge()
        {
            var originalObject = new OriginalType();
            var wrappedObject = _bridge.Wrap<WrapperForOriginalTypeWithBridge, OriginalType>(originalObject);
            Assert.NotNull(wrappedObject.Bridge);
        }

        [Fact]
        public void WrapsAndUnwrapsArray()
        {
            var originalObjects = new[] { new OriginalType() };
            var wrappedObjects = _bridge.Wrap<WrapperForOriginalType, OriginalType>(originalObjects);
            Assert.Single(wrappedObjects);
            Assert.Equal(originalObjects[0], _bridge.Unwrap<OriginalType>(wrappedObjects)[0]);
        }

        [Fact]
        public void WrapsNullAsNull()
        {
            OriginalType obj = null;
            Assert.Null(_bridge.Wrap<WrapperForOriginalType, OriginalType>(obj));
        }

        [Fact]
        public void WrapsNullArrayAsNull()
        {
            OriginalType[] obj = null;
            Assert.Null(_bridge.Wrap<WrapperForOriginalType, OriginalType>(obj));
        }

        [Fact]
        public void UnwrapsNullAsNull()
        {
            WrapperForOriginalType obj = null;
            Assert.Null(_bridge.Unwrap<OriginalType>(obj));
        }

        [Fact]
        public void UnwrapsNullArrayAsNull()
        {
            WrapperForOriginalType[] obj = null;
            Assert.Null(_bridge.Unwrap<OriginalType>(obj));
        }

        //[Fact]
        //public void CreatesBridgeWithMockDependency()
        //{
        //    var mockDep = new Mock<ISomeDependency>();
        //    var container = new Container(cfg =>
        //    {
        //        cfg.For<ISomeDependency>().Use(mockDep.Object);
        //    });
        //    var bridge = new TfsApiBridge(container);
        //    Assert.NotNull(bridge);
        //}

        public class OriginalType
        {
            public static int counter;
            public static object lockObject = new object();
            private readonly int _id;
            public OriginalType()
            {
                lock (lockObject)
                {
                    _id = ++counter;
                }
            }
            public override bool Equals(object obj) => obj is OriginalType && ((OriginalType)obj)._id == _id;
            public override int GetHashCode() => _id;
            public override string ToString() => "OriginalObject:" + _id;
        }
        private interface IOriginalType { }
        public class WrapperForOriginalType : WrapperFor<OriginalType>, IOriginalType
        {
            public WrapperForOriginalType(OriginalType o) : base(o) { }
        }
        public class WrapperForOriginalTypeWithBridge : WrapperFor<OriginalType>, IOriginalType
        {
            public WrapperForOriginalTypeWithBridge(OriginalType o, TfsApiBridge b) : base(o)
            {
                Bridge = b;
            }
            public TfsApiBridge Bridge { get; private set; }
        }

        public enum OriginalEnum
        {
            Value1,
            Value2,
        };

        public enum WrappedEnum
        {
            Value1,
            Value2,
        };
    }
}
