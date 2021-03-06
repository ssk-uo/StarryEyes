﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using StarryEyes.Models.Receivers.ReceiveElements;

namespace StarryEyes.Models.Receivers.Managers
{
    internal class StreamTrackReceiveManager
    {
        private readonly UserReceiveManager _receiveManager;

        private readonly object _trackLocker = new object();

        private readonly SortedDictionary<string, long> _trackResolver = new SortedDictionary<string, long>();

        private readonly SortedDictionary<string, int> _trackReferenceCount = new SortedDictionary<string, int>();

        private readonly object _danglingLocker = new object();

        private readonly List<string> _danglingKeywords = new List<string>();

        private bool _isDanglingNotified;

        private readonly object _addTrackLocker = new object();

        private List<string> _addTrackWaits;

        public StreamTrackReceiveManager(UserReceiveManager receiveManager)
        {
            _receiveManager = receiveManager;
            _receiveManager.TrackRearranged += UpdateTrackInfo;
        }

        void UpdateTrackInfo()
        {
            string[] dang;
            lock (_trackLocker)
            {
                var allTracks = _trackResolver.Keys.ToArray();
                _trackResolver.Clear();
                var trackers = _receiveManager.GetTrackers();
                foreach (var track in trackers)
                {
                    var id = track.UserId;
                    track.TrackKeywords.ForEach(k => _trackResolver[k] = id);
                }
                dang = allTracks.Except(_trackResolver.Keys).ToArray();
            }
            if (dang.Length > 0)
            {
                lock (_danglingLocker)
                {
                    _danglingKeywords.AddRange(dang);
                    NotifyDangling();
                }
            }
        }

        public void AddTrackKeyword(string track)
        {
            lock (_addTrackLocker)
            {
                if (_addTrackWaits == null)
                {
                    _addTrackWaits = new List<string> { track };
                    Observable.Timer(TimeSpan.FromSeconds(3))
                              .Subscribe(_ =>
                              {
                                  List<string> tracks;
                                  lock (_addTrackLocker)
                                  {
                                      tracks = _addTrackWaits;
                                      _addTrackWaits = null;
                                  }
                                  AddTrackKeywordCore(tracks.ToArray());
                              });
                }
                else
                {
                    _addTrackWaits.Add(track);
                }
            }
        }

        public void RemoveTrackKeyword(string track)
        {
            Observable.Timer(TimeSpan.FromSeconds(10))
                      .Subscribe(_ => RemoveTrackKeywordCore(track));
        }

        private void AddTrackKeywordCore(string[] tracks)
        {
            lock (_trackLocker)
            {
                foreach (var track in tracks)
                {
                    if (_trackReferenceCount.ContainsKey(track))
                    {
                        _trackReferenceCount[track]++;
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine("◎ track add: " + track);
                    _trackReferenceCount[track] = 1;
                }

                var trackList = new List<string>(tracks);

                while (trackList.Count > 0)
                {
                    var tracker = _receiveManager.GetSuitableKeywordTracker();
                    if (tracker == null)
                    {
                        lock (_danglingLocker)
                        {
                            _danglingKeywords.AddRange(trackList);
                        }
                        NotifyDangling();
                        return;
                    }
                    var acceptableCount = UserStreamsReceiver.MaxTrackingKeywordCounts - tracker.TrackKeywords.Count();
                    var acceptables = trackList.Take(acceptableCount).ToArray();
                    acceptables.ForEach(track => _trackResolver[track] = tracker.UserId);
                    tracker.TrackKeywords = tracker.TrackKeywords.Concat(acceptables).ToArray();
                    trackList = trackList.Skip(acceptableCount).ToList();
                }
            }
        }

        private void RemoveTrackKeywordCore(string track)
        {
            lock (_trackLocker)
            {
                if (!_trackReferenceCount.ContainsKey(track) || --_trackReferenceCount[track] > 0)
                {
                    return;
                }
                _trackReferenceCount.Remove(track);
                if (_trackResolver.ContainsKey(track))
                {
                    var id = _trackResolver[track];
                    _trackResolver.Remove(track);
                    var tracker = _receiveManager.GetKeywordTrackerFromId(id);
                    tracker.TrackKeywords = tracker.TrackKeywords.Except(new[] { track });
                    return;
                }
            }
            lock (_danglingLocker)
            {
                _danglingKeywords.Remove(track);
            }
        }

        private void NotifyDangling()
        {
            if (_isDanglingNotified) return;
            _isDanglingNotified = true;
        }

        private void ResolveDanglings()
        {
        }

    }
}
