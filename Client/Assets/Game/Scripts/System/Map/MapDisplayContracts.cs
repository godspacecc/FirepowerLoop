using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public readonly struct MapOpenRequest
    {
        public MapOpenRequest(int mapItemId, string mapId) { MapItemId = mapItemId; MapId = mapId ?? string.Empty; }
        public int MapItemId { get; }
        public string MapId { get; }
    }

    public enum MapOpenFailureCode
    {
        None, NotCarried, NoCurrentMap, NoCurrentMapItem, InvalidItemAction, DisplayUnavailable
    }

    public readonly struct MapPlayerMarkerSnapshot
    {
        public MapPlayerMarkerSnapshot(Vector3 worldPosition, float yaw) { WorldPosition = worldPosition; Yaw = yaw; }
        public Vector3 WorldPosition { get; }
        public float Yaw { get; }
    }

    public readonly struct MapExtractionMarkerSnapshot
    {
        public MapExtractionMarkerSnapshot(string id, Vector3 worldPosition, bool isLive)
        { Id = id ?? string.Empty; WorldPosition = worldPosition; IsLive = isLive; }
        public string Id { get; }
        public Vector3 WorldPosition { get; }
        public bool IsLive { get; }
    }

    public sealed class MapDisplaySnapshot
    {
        public MapDisplaySnapshot(MapOpenRequest request, MapDisplayDefinition display,
            MapPlayerMarkerSnapshot? player, IEnumerable<MapExtractionMarkerSnapshot> extractions)
        {
            Request = request;
            Display = display;
            Player = player;
            Extractions = Array.AsReadOnly(extractions == null
                ? Array.Empty<MapExtractionMarkerSnapshot>()
                : new List<MapExtractionMarkerSnapshot>(extractions).ToArray());
        }
        public MapOpenRequest Request { get; }
        public MapDisplayDefinition Display { get; }
        public MapPlayerMarkerSnapshot? Player { get; }
        public IReadOnlyList<MapExtractionMarkerSnapshot> Extractions { get; }
    }
}
