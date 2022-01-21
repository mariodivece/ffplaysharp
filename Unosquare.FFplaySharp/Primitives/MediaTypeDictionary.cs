namespace Unosquare.FFplaySharp.Primitives;

public class MediaTypeDictionary<T> : Dictionary<AVMediaType, T?>
{
    public const int MediaTypeCount = (int)AVMediaType.AVMEDIA_TYPE_NB;

    public static readonly IReadOnlyList<AVMediaType> MediaTypes = Enum.GetValues<AVMediaType>()
        .Where(c => c is not (AVMediaType.AVMEDIA_TYPE_NB or AVMediaType.AVMEDIA_TYPE_UNKNOWN))
        .ToArray();

    public MediaTypeDictionary(T? defaultValue = default)
        : base(MediaTypeCount)
    {
        DefaultValue = defaultValue;
        Clear();
    }

    public T? DefaultValue { get; }

    public new T? this[AVMediaType mediaType]
    {
        get => TryGetValue(mediaType, out var value) ? value : DefaultValue;
        set => base[mediaType] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasValue(AVMediaType mediaType) => !Equals(this[mediaType], DefaultValue);

    public bool HasAudio => HasValue(AVMediaType.AVMEDIA_TYPE_AUDIO);

    public bool HasAttachment => HasValue(AVMediaType.AVMEDIA_TYPE_ATTACHMENT);

    public bool HasData => HasValue(AVMediaType.AVMEDIA_TYPE_DATA);

    public bool HasSubtitle => HasValue(AVMediaType.AVMEDIA_TYPE_SUBTITLE);

    public bool HasVideo => HasValue(AVMediaType.AVMEDIA_TYPE_VIDEO);

    public T? Audio
    {
        get => this[AVMediaType.AVMEDIA_TYPE_AUDIO];
        set => this[AVMediaType.AVMEDIA_TYPE_AUDIO] = value;
    }

    

    public T? Attachment
    {
        get => this[AVMediaType.AVMEDIA_TYPE_ATTACHMENT];
        set => this[AVMediaType.AVMEDIA_TYPE_ATTACHMENT] = value;
    }



    public T? Data
    {
        get => this[AVMediaType.AVMEDIA_TYPE_DATA];
        set => this[AVMediaType.AVMEDIA_TYPE_DATA] = value;
    }



    public T? Subtitle
    {
        get => this[AVMediaType.AVMEDIA_TYPE_SUBTITLE];
        set => this[AVMediaType.AVMEDIA_TYPE_SUBTITLE] = value;
    }

    

    public T? Video
    {
        get => this[AVMediaType.AVMEDIA_TYPE_VIDEO];
        set => this[AVMediaType.AVMEDIA_TYPE_VIDEO] = value;
    }

    
}
