using System;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>
    /// Delimits the replay of a quad. Begin puts PQS in the state a real build is in when it hands a vertex
    /// over, having first saved everything a calibration writes over: the vertices of the quad and of PQS,
    /// the build state of PQS and of its vertex build data, and whether the quad is built. End puts all of
    /// it back, so that neither the terrain nor the rest of the build depends on what was replayed. Meant
    /// to be allocated once and reused from one calibrated quad to the next.
    /// </summary>
    internal sealed class ReplayScope
    {
        private readonly AccessTools.FieldRef<PQS, PQ> _buildQuadField;
        private readonly AccessTools.FieldRef<PQS, int> _vertexIndexField;

        // The replay under way, if any. Cleared by End, so that no quad is held on to between two
        // calibrations.
        private PQS _sphere;
        private PQ _quad;
        private PQS.VertexBuildData _data;
        private int _count;

        // What Begin found, and End puts back.
        private Vector3[] _quadVerts = new Vector3[0];
        private Vector3d[] _sphereVerts = new Vector3d[0];
        private PQ _buildQuad;
        private int _vertexIndex;
        private PQ _dataQuad;
        private Vector3d _direction;
        private double _height;
        private int _vertIndex;
        private bool _isBuilt;

        /// <summary>Throws if the fields of PQS it saves cannot be reached.</summary>
        public ReplayScope()
        {
            _buildQuadField = AccessTools.FieldRefAccess<PQS, PQ>("buildQuad");
            _vertexIndexField = AccessTools.FieldRefAccess<PQS, int>("vertexIndex");
        }

        /// <summary>Whether a replay has begun and not ended yet.</summary>
        public bool IsActive => _quad != null;

        /// <summary>
        /// Saves the state of the given quad, sphere and vertex build data, the first count vertices only,
        /// then points PQS and the vertex build data at the quad, the way a real build is when it hands a
        /// vertex over. Both vertex arrays must hold at least count vertices. Must be followed by End.
        /// </summary>
        public void Begin(PQS sphere, PQ quad, PQS.VertexBuildData data, int count)
        {
            // Grown only when a longer quad comes along, so that calibrating allocates nothing past the
            // first quad: a collection triggered here could land inside a timed round.
            if (_quadVerts.Length < count)
            {
                _quadVerts = new Vector3[count];
                _sphereVerts = new Vector3d[count];
            }

            Array.Copy(quad.verts, _quadVerts, count);
            Array.Copy(PQS.verts, _sphereVerts, count);
            _buildQuad = _buildQuadField(sphere);
            _vertexIndex = _vertexIndexField(sphere);
            _dataQuad = data.buildQuad;
            _direction = data.directionFromCenter;
            _height = data.vertHeight;
            _vertIndex = data.vertIndex;
            _isBuilt = quad.isBuilt;

            // BuildQuad clears buildQuad on its way out, so it has to be put back for the replay.
            _buildQuadField(sphere) = quad;
            data.buildQuad = quad;

            // Last, so that the scope only becomes active once everything above has gone through.
            _sphere = sphere;
            _data = data;
            _count = count;
            _quad = quad;
        }

        /// <summary>Puts back everything the last Begin found, then lets go of the quad.</summary>
        public void End()
        {
            Array.Copy(_quadVerts, _quad.verts, _count);
            Array.Copy(_sphereVerts, PQS.verts, _count);
            _buildQuadField(_sphere) = _buildQuad;
            _vertexIndexField(_sphere) = _vertexIndex;
            _data.buildQuad = _dataQuad;
            _data.directionFromCenter = _direction;
            _data.vertHeight = _height;
            _data.vertIndex = _vertIndex;
            _quad.isBuilt = _isBuilt;

            _sphere = null;
            _quad = null;
            _data = null;
            _buildQuad = null;
            _dataQuad = null;
        }
    }
}
