using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PoseRuntime
{
    /// <summary>
    /// Snapshot of seating occupancy parsed from skeleton metadata.
    /// </summary>
    public readonly struct SeatingSnapshot
    {
        private readonly Dictionary<string, bool> _occupancy;
        private readonly List<string> _order;

        public SeatingSnapshot(string activeSeatId, float confidence, Dictionary<string, bool> occupancy, List<string> order)
        {
            ActiveSeatId = activeSeatId;
            Confidence = confidence;
            _occupancy = occupancy ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _order = order ?? new List<string>();
        }

        public string ActiveSeatId { get; }
        public float Confidence { get; }
        public IReadOnlyDictionary<string, bool> Occupancy => _occupancy;
        public IReadOnlyList<string> SeatOrder => _order;
        public bool HasAnyHuman => _occupancy != null && _occupancy.Values.Any(value => value);

        public bool TryGetOccupancy(string seatId, out bool occupied)
        {
            occupied = false;
            if (_occupancy == null || string.IsNullOrEmpty(seatId))
            {
                return false;
            }

            return _occupancy.TryGetValue(seatId, out occupied);
        }
    }

    public static class SeatingMetadataUtility
    {
        public static bool TryGetSnapshot(SkeletonSample sample, out SeatingSnapshot snapshot)
        {
            snapshot = default;
            if (sample?.Meta == null)
            {
                return false;
            }

            if (!sample.Meta.TryGetValue("seating", out var raw) || raw == null)
            {
                return false;
            }

            return TryParseSnapshot(raw, out snapshot);
        }

        private static bool TryParseSnapshot(object raw, out SeatingSnapshot snapshot)
        {
            snapshot = default;
            if (raw == null)
            {
                return false;
            }

            if (raw is SeatingSnapshot existing)
            {
                snapshot = existing;
                return true;
            }

            try
            {
                JObject token = raw as JObject;
                if (token == null)
                {
                    if (raw is string rawString && !string.IsNullOrWhiteSpace(rawString))
                    {
                        token = JObject.Parse(rawString);
                    }
                    else if (raw is IDictionary<string, object> dict)
                    {
                        token = JObject.FromObject(dict);
                    }
                    else if (raw is JToken jToken)
                    {
                        token = jToken as JObject;
                    }
                    else
                    {
                        token = JObject.FromObject(raw);
                    }
                }

                if (token == null)
                {
                    return false;
                }

                snapshot = FromJObject(token);
                return true;
            }
            catch (Exception)
            {
                snapshot = default;
                return false;
            }
        }

        private static SeatingSnapshot FromJObject(JObject obj)
        {
            var occupancy = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            var seatsArray = obj["seats"] as JArray;
            if (seatsArray != null)
            {
                foreach (var token in seatsArray)
                {
                    var id = token.Value<string>("id");
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var occupied = token.Value<bool?>("occupied") ?? false;
                    occupancy[id] = occupied;
                    order.Add(id);
                }
            }

            var occupiedIds = obj["occupiedSeatIds"] as JArray;
            if (occupiedIds != null)
            {
                foreach (var idToken in occupiedIds)
                {
                    var id = idToken.Value<string>() ?? string.Empty;
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    occupancy[id] = true;
                    if (!order.Contains(id))
                    {
                        order.Add(id);
                    }
                }
            }

            var occupancyObject = obj["occupancy"] as JObject;
            if (occupancyObject != null)
            {
                foreach (var property in occupancyObject.Properties())
                {
                    var id = property.Name;
                    if (!occupancy.ContainsKey(id))
                    {
                        order.Add(id);
                    }

                    occupancy[id] = property.Value.Value<bool?>() ?? false;
                }
            }

            var activeSeatId = obj.Value<string>("activeSeatId");
            if (!string.IsNullOrEmpty(activeSeatId) && !occupancy.ContainsKey(activeSeatId))
            {
                occupancy[activeSeatId] = true;
                order.Add(activeSeatId);
            }

            var confidence = (float)(obj.Value<double?>("confidence") ?? (string.IsNullOrEmpty(activeSeatId) ? 0.0 : 1.0));

            return new SeatingSnapshot(activeSeatId, confidence, occupancy, order);
        }
    }
}
