using StructureMap;

namespace GitTfs.Util
{
    public class GitTfsCommandFactory
    {
        private readonly IContainer _container;

        public GitTfsCommandFactory(IContainer container)
        {
            _container = container;
        }

        public GitTfsCommand GetCommand(string name)
        {
            return _container.TryGetInstance<GitTfsCommand>(name);
        }
    }
}
