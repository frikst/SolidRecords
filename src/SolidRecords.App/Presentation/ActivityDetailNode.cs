using System.Collections.ObjectModel;
using System.Windows.Media;

namespace SolidRecords.App.Presentation;

public sealed class ActivityDetailNode(string id) : ObservableObject
{
    private string _label = "";
    private string _summary = "";
    private string _description = "";
    private ImageSource? _icon;

    public string Id { get; } = id;
    public string Label { get => _label; private set => Set(ref _label, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Description { get => _description; private set => Set(ref _description, value); }
    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }
    public ObservableCollection<ActivityDetailNode> Children { get; } = [];

    public void Update(string label, string summary, string description, ImageSource? icon = null)
    {
        Label = label;
        Summary = summary;
        Description = description;
        Icon = icon;
    }

    public static void Synchronize(ObservableCollection<ActivityDetailNode> nodes, IReadOnlyList<string> ids,
        Action<ActivityDetailNode, int> update)
    {
        var wanted = ids.ToHashSet();
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (!wanted.Contains(nodes[i].Id)) nodes.RemoveAt(i);
        var existing = nodes.ToDictionary(node => node.Id);
        for (var i = 0; i < ids.Count; i++)
        {
            if (!existing.TryGetValue(ids[i], out var node))
            {
                node = new(ids[i]);
                nodes.Insert(i, node);
            }
            else if (!ReferenceEquals(nodes[i], node)) nodes.Move(nodes.IndexOf(node), i);
            update(node, i);
        }
    }
}
