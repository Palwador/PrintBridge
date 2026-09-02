using SolidWorks.Interop.sldworks;

namespace SwPrototypeExporter
{
    internal sealed class BodyExportItem
    {
        public BodyExportItem(Body2 body, Component2 component, string displayName, string fileStemName, bool requiresTemporaryPart)
        {
            Body = body;
            Component = component;
            DisplayName = displayName;
            FileStemName = fileStemName;
            RequiresTemporaryPart = requiresTemporaryPart;
        }

        public Body2 Body { get; private set; }
        public Component2 Component { get; private set; }
        public string DisplayName { get; private set; }
        public string FileStemName { get; private set; }
        public bool RequiresTemporaryPart { get; private set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
