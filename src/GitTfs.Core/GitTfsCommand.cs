using NDesk.Options;
using StructureMap;

namespace GitTfs
{
    public interface GitTfsCommand
    {
        OptionSet OptionSet { get; }
    }
}
