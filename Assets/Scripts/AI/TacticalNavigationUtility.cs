using Character;
using Combat;
using Enemies;
using UnityEngine;
using UnityEngine.AI;

namespace AI
{
    /// <summary>
    /// Shared zero-allocation NavMesh spatial reasoning helper powering both
    /// EnemyController and SquadMemberController Behavior Trees (cover search,
    /// line-of-sight blocking checks, flanking arc sampling, and safe NavMesh warping).
    /// </summary>
    public static class TacticalNavigationUtility
    {
        private static readonly RaycastHit[] LosBuffer = new RaycastHit[16];
        private static readonly Collider[] CoverOverlapBuffer = new Collider[40];
        private static readonly NavMeshPath ScratchPath = new();

        public static bool HasClearLineOfSight(Vector3 originPos, Vector3 targetPos)
        {
            var start = originPos + Vector3.up * 1.15f;
            var end = targetPos + Vector3.up * 1.0f;
            var delta = end - start;
            var dist = delta.magnitude;
            if (dist < 0.6f) return true;

            var hits = Physics.RaycastNonAlloc(start, delta / dist, LosBuffer, dist - 0.35f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits; i++)
            {
                var col = LosBuffer[i].collider;
                if (col == null) continue;
                if (col.GetComponentInParent<PlayerController>() != null ||
                    col.GetComponentInParent<SquadMemberController>() != null ||
                    col.GetComponentInParent<EnemyController>() != null ||
                    col.GetComponentInParent<Projectile>() != null)
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        public static bool EnsureAgentOnNavMesh(NavMeshAgent agent, Vector3 desiredPos, float sampleRadius = 6f)
        {
            if (agent == null || !agent.enabled || !agent.gameObject.activeInHierarchy) return false;
            if (!NavMesh.SamplePosition(desiredPos, out var hit, sampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            if (!agent.isOnNavMesh || (agent.transform.position - hit.position).sqrMagnitude > 0.12f)
            {
                agent.Warp(hit.position);
            }
            return agent.isOnNavMesh;
        }

        public static bool TryGetReachablePoint(Vector3 originPos, Vector3 candidatePos, out Vector3 navPoint, float sampleRadius = 3.2f)
        {
            navPoint = candidatePos;
            if (!NavMesh.SamplePosition(originPos, out var originHit, 3.0f, NavMesh.AllAreas))
            {
                return false;
            }
            if (!NavMesh.SamplePosition(candidatePos, out var targetHit, sampleRadius, NavMesh.AllAreas))
            {
                return false;
            }
            if (!NavMesh.CalculatePath(originHit.position, targetHit.position, NavMesh.AllAreas, ScratchPath))
            {
                return false;
            }
            if (ScratchPath.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            navPoint = targetHit.position;
            return true;
        }

        public static bool FindTacticalCoverPosition(
            Vector3 agentPos,
            Vector3 threatPos,
            float searchRadius,
            float minThreatDistance,
            out Vector3 coverWorldPos)
        {
            coverWorldPos = agentPos;
            var envLayer = LayerMask.NameToLayer("Environment");
            var mask = envLayer >= 0 ? (1 << envLayer) : ~0;

            var count = Physics.OverlapSphereNonAlloc(agentPos, searchRadius, CoverOverlapBuffer, mask, QueryTriggerInteraction.Ignore);
            var bestScore = float.MaxValue;
            var found = false;

            for (var i = 0; i < count; i++)
            {
                var col = CoverOverlapBuffer[i];
                if (col == null) continue;
                var b = col.bounds;
                // Filter out flat deck floor slabs and giant outer sky/perimeter hulls
                if (b.size.y is < 0.72f or > 7.0f) continue;
                if (b.size is { x: > 13.5f, z: > 13.5f }) continue;

                var center = b.center;
                center.y = agentPos.y;
                var awayFromThreat = center - threatPos;
                awayFromThreat.y = 0f;
                if (awayFromThreat.sqrMagnitude < 0.25f) continue;
                awayFromThreat.Normalize();

                var halfSpan = Mathf.Max(b.extents.x, b.extents.z);
                var candidate = center + awayFromThreat * (halfSpan + 0.95f);
                candidate.y = agentPos.y;

                var threatDistSq = (candidate - threatPos).sqrMagnitude;
                if (threatDistSq < minThreatDistance * minThreatDistance) continue;

                if (!TryGetReachablePoint(agentPos, candidate, out var validPoint, 2.2f)) continue;
                if (HasClearLineOfSight(validPoint, threatPos)) continue;

                var moveCostSq = (validPoint - agentPos).sqrMagnitude;
                if (!(moveCostSq < bestScore)) continue;
                bestScore = moveCostSq;
                coverWorldPos = validPoint;
                found = true;
            }

            return found;
        }

        public static bool FindFlankPosition(
            Vector3 agentPos,
            Vector3 targetPos,
            float flankSign,
            float orbitRadius,
            out Vector3 flankWorldPos)
        {
            flankWorldPos = targetPos;
            var fromTarget = agentPos - targetPos;
            fromTarget.y = 0f;
            if (fromTarget.sqrMagnitude < 0.05f) fromTarget = Vector3.back;
            fromTarget.Normalize();

            var dirToAgent = fromTarget;
            float[] angles = { 54f * flankSign, 82f * flankSign, -54f * flankSign };
            foreach (var deg in angles)
            {
                var offsetDir = Quaternion.Euler(0f, deg, 0f) * dirToAgent;
                var candidate = targetPos + offsetDir * orbitRadius;
                if (!TryGetReachablePoint(agentPos, candidate, out var validPoint, 3.0f)) continue;
                flankWorldPos = validPoint;
                return true;
            }
            return false;
        }
    }
}
