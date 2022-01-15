namespace Unosquare.FFplaySharp.Primitives;

public class MediaTypeDictionary<T> : Dictionary<AVMediaType, T>
{
    public const int MediaTypeCount = (int)AVMediaType.AVMEDIA_TYPE_NB;

    public static readonly IEnumerable<AVMediaType> MediaTypes;

    static MediaTypeDictionary()
    {
        var result = new List<AVMediaType>(MediaTypeCount);
        for (var i = 0; i < MediaTypeCount; i++)
            result.Add((AVMediaType)i);

        MediaTypes = result;
    }

    public MediaTypeDictionary(T defaultValue = default)
        : base(MediaTypeCount)
    {
        DefaultValue = defaultValue;
        Clear();
    }

    public T DefaultValue { get; }

    public T this[int mediaType]
    {
        get => this[(AVMediaType)mediaType];
        set => this[(AVMediaType)mediaType] = value;
    }

    public new void Clear()
    {
        base.Clear();
        foreach (var m in MediaTypes)
            this[m] = DefaultValue;
    }

    public bool HasValue(AVMediaType mediaType) => (object)this[mediaType] != (object)DefaultValue;

    public T Audio
    {
        get => this[AVMediaType.AVMEDIA_TYPE_AUDIO];
        set => this[AVMediaType.AVMEDIA_TYPE_AUDIO] = value;
    }

    public bool HasAudio => (object)Audio != (object)DefaultValue;

    public T Attachment
    {
        get => this[AVMediaType.AVMEDIA_TYPE_ATTACHMENT];
        set => this[AVMediaType.AVMEDIA_TYPE_ATTACHMENT] = value;
    }

    public bool HasAttachment => (object)Attachment != (object)DefaultValue;

    public T Data
    {
        get => this[AVMediaType.AVMEDIA_TYPE_DATA];
        set => this[AVMediaType.AVMEDIA_TYPE_DATA] = value;
    }

    public bool HasData => (object)Data != (object)DefaultValue;

    public T Subtitle
    {
        get => this[AVMediaType.AVMEDIA_TYPE_SUBTITLE];
        set => this[AVMediaType.AVMEDIA_TYPE_SUBTITLE] = value;
    }

    public bool HasSubtitle => (object)Subtitle != (object)DefaultValue;

    public T Video
    {
        get => this[AVMediaType.AVMEDIA_TYPE_VIDEO];
        set => this[AVMediaType.AVMEDIA_TYPE_VIDEO] = value;
    }

    public bool HasVideo => (object)Video != (object)DefaultValue;
}
