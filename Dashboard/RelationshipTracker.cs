using System;
using System.Collections.Generic;
using System.Linq;

namespace Dashboard
{
    /// <summary>
    /// Tracks and analyzes relationships between NPCs
    /// </summary>
    public static class RelationshipTracker
    {
        public class RelationshipNode
        {
            public int id;
            public string name;
            public string occupation;
            public int connectionCount;
            public string photoBase64;
        }

        public class RelationshipEdge
        {
            public int sourceId;
            public int targetId;
            public float strength; // 0-1 scale
            public string type; // "friend", "colleague", "family", "romantic", "acquaintance"
            public bool mutual; // Is the relationship reciprocated?
        }

        public class RelationshipGraph
        {
            public List<RelationshipNode> nodes = new List<RelationshipNode>();
            public List<RelationshipEdge> edges = new List<RelationshipEdge>();
        }

        /// <summary>
        /// Get relationship graph for a specific NPC and their connections
        /// </summary>
        public static RelationshipGraph GetRelationshipGraph(int npcId, int depth = 1)
        {
            var graph = new RelationshipGraph();
            var processedIds = new HashSet<int>();
            var queue = new Queue<(int id, int currentDepth)>();

            queue.Enqueue((npcId, 0));

            while (queue.Count > 0)
            {
                var (currentId, currentDepth) = queue.Dequeue();

                if (processedIds.Contains(currentId) || currentDepth > depth)
                    continue;

                processedIds.Add(currentId);

                if (!NpcCache.TryGetCitizen(currentId, out var citizen) || citizen == null)
                    continue;

                // Add node - use cached photo from NpcCache
                string photoBase64 = null;
                var cachedNpc = NpcCache.Snapshot().FirstOrDefault(n => n.id == currentId);
                if (cachedNpc != null)
                {
                    photoBase64 = cachedNpc.photoBase64;
                }
                
                var node = new RelationshipNode
                {
                    id = currentId,
                    name = GetFullName(citizen),
                    occupation = citizen.job?.employer?.name ?? citizen.job?.preset?.name ?? "Unemployed",
                    connectionCount = 0,
                    photoBase64 = photoBase64
                };
                graph.nodes.Add(node);

                // Get all acquaintances
                var acquaintances = citizen.acquaintances;
                if (acquaintances != null)
                {
                    foreach (var connection in acquaintances)
                    {
                        if (connection == null || connection.with == null)
                            continue;

                        var other = connection.with;
                        var otherId = other.humanID;
                        
                        // Create edge
                        var edge = new RelationshipEdge
                        {
                            sourceId = currentId,
                            targetId = otherId,
                            strength = connection.known,
                            type = DetermineRelationshipType(citizen, other),
                            mutual = IsRelationshipMutual(citizen, other)
                        };
                        graph.edges.Add(edge);
                        node.connectionCount++;

                        // Add to queue for next depth level
                        if (currentDepth < depth)
                        {
                            queue.Enqueue((otherId, currentDepth + 1));
                        }
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// Get relationship graph for entire city (expensive!)
        /// </summary>
        public static RelationshipGraph GetCityRelationshipGraph(int maxNodes = 100)
        {
            var graph = new RelationshipGraph();
            var citizens = NpcCache.Snapshot().Take(maxNodes).ToList();

            // Add all nodes
            foreach (var npc in citizens)
            {
                if (!NpcCache.TryGetCitizen(npc.id, out var citizen) || citizen == null)
                    continue;

                // Add node - use cached photo from NpcCache
                string photoBase64 = null;
                var cachedNpc = NpcCache.Snapshot().FirstOrDefault(n => n.id == npc.id);
                if (cachedNpc != null)
                {
                    photoBase64 = cachedNpc.photoBase64;
                }
                
                var node = new RelationshipNode
                {
                    id = npc.id,
                    name = GetFullName(citizen),
                    occupation = citizen.job?.employer?.name ?? citizen.job?.preset?.name ?? "Unemployed",
                    connectionCount = 0,
                    photoBase64 = photoBase64
                };
                graph.nodes.Add(node);
            }

            // Add edges
            var processedPairs = new HashSet<string>();
            foreach (var npc in citizens)
            {
                if (!NpcCache.TryGetCitizen(npc.id, out var citizen) || citizen == null)
                    continue;

                var acquaintances = citizen.acquaintances;
                if (acquaintances == null)
                    continue;

                foreach (var connection in acquaintances)
                {
                    if (connection == null || connection.with == null)
                        continue;

                    var other = connection.with;
                    var otherId = other.humanID;
                    
                    // Only include if other is in our node list
                    if (!graph.nodes.Any(n => n.id == otherId))
                        continue;

                    // Avoid duplicate edges
                    var pairKey = $"{Math.Min(citizen.humanID, otherId)}-{Math.Max(citizen.humanID, otherId)}";
                    if (processedPairs.Contains(pairKey))
                        continue;

                    processedPairs.Add(pairKey);

                    var edge = new RelationshipEdge
                    {
                        sourceId = citizen.humanID,
                        targetId = otherId,
                        strength = connection.known,
                        type = DetermineRelationshipType(citizen, other),
                        mutual = IsRelationshipMutual(citizen, other)
                    };
                    graph.edges.Add(edge);

                    // Increment connection counts
                    var sourceNode = graph.nodes.FirstOrDefault(n => n.id == citizen.humanID);
                    var targetNode = graph.nodes.FirstOrDefault(n => n.id == otherId);
                    if (sourceNode != null) sourceNode.connectionCount++;
                    if (targetNode != null) targetNode.connectionCount++;
                }
            }

            return graph;
        }

        private static string GetFullName(Human human)
        {
            var name = (human.firstName ?? "") + " " + (human.surName ?? "");
            return name.Trim();
        }

        private static string DetermineRelationshipType(Human human1, Human human2)
        {
            // Check for romantic relationship (partners)
            if (human1.partner != null && human1.partner.humanID == human2.humanID)
                return "romantic";

            // Check for roommates (same residence)
            if (human1.residence != null && human2.residence != null && 
                human1.residence == human2.residence)
                return "roommate";

            // Check for colleagues (same workplace)
            if (human1.job?.employer != null && human2.job?.employer != null &&
                human1.job.employer == human2.job.employer)
                return "colleague";

            // Check for strong connection via acquaintances list
            if (human1.acquaintances != null)
            {
                foreach (var acq in human1.acquaintances)
                {
                    if (acq != null && acq.with != null && acq.with.humanID == human2.humanID && acq.known > 0.7f)
                        return "friend";
                }
            }

            return "acquaintance";
        }

        private static bool IsRelationshipMutual(Human human1, Human human2)
        {
            if (human2.acquaintances == null)
                return false;

            foreach (var acq in human2.acquaintances)
            {
                if (acq != null && acq.with != null && acq.with.humanID == human1.humanID)
                    return true;
            }
            
            return false;
        }
    }
}
