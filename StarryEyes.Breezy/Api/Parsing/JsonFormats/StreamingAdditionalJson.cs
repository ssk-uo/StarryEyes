﻿using StarryEyes.Breezy.DataModel;

namespace StarryEyes.Breezy.Api.Parsing.JsonFormats
{
    public class StreamingAdditionalJson : ITwitterStreamingElementSpawnable
    {
        public long[] friends { get; set; }

        public TwitterStreamingElement Spawn()
        {
            return new TwitterStreamingElement()
            {
                EventType = DataModel.EventType.Empty,
                Enumeration = friends,
            };
        }
    }

    public class StreamingDeleteJson : ITwitterStreamingElementSpawnable
    {
        public StreamingDeleteContentJson delete { get; set; }

        public TwitterStreamingElement Spawn()
        {
            return delete.Spawn();
        }
    }

    public class StreamingDeleteContentJson : ITwitterStreamingElementSpawnable
    {
        public StreamingIdJson status { get; set; }

        public StreamingIdJson direct_message { get; set; }

        public TwitterStreamingElement Spawn()
        {
            return new TwitterStreamingElement()
            {
                EventType = EventType.Empty,
                DeletedId = (status != null ? long.Parse(status.id_str) :
                            (direct_message != null ? long.Parse(direct_message.id_str) : 0)),
            };
        }
    }

    public class StreamingIdJson
    {
        public string id_str { get; set; }
    }

    public class StreamingTrackJson
    {
        public int track { get; set; }
    }
}