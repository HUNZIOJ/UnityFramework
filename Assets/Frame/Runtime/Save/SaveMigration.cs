using System;

namespace Frame.Save
{
    public sealed class SaveMigration<TData>
    {
        private readonly Func<TData, TData> migrate;

        public SaveMigration(int fromVersion, int toVersion, Func<TData, TData> migrate)
        {
            if (toVersion <= fromVersion)
            {
                throw new ArgumentException("Save migration target version must be greater than source version.", "toVersion");
            }

            if (migrate == null)
            {
                throw new ArgumentNullException("migrate");
            }

            FromVersion = fromVersion;
            ToVersion = toVersion;
            this.migrate = migrate;
        }

        public int FromVersion { get; private set; }

        public int ToVersion { get; private set; }

        public TData Apply(TData data)
        {
            return migrate(data);
        }
    }
}
