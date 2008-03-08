﻿#define DEBUG

using System;
using System.Collections.Generic;
using System.Text;

using Box2DX.Common;

namespace Box2DX.Collision
{
	/// <summary>
	/// Convex polygon. The vertices must be in CCW order for a right-handed
	/// coordinate system with the z-axis coming out of the screen.
	/// </summary>
	public class PolygonDef : ShapeDef
	{
		/// <summary>
		/// The number of polygon vertices.
		/// </summary>
		public int VertexCount;

		/// <summary>
		/// The polygon vertices in local coordinates.
		/// </summary>
		public Vector2[] Vertices = new Vector2[Settings.MaxPolygonVertices];

		public PolygonDef()
		{
			Type = e_polygonShape;
			VertexCount = 0;
		}

		/// <summary>
		/// Build vertices to represent an axis-aligned box.
		/// </summary>
		/// <param name="hx">The half-width</param>
		/// <param name="hy">The half-height.</param>
		public void SetAsBox(float hx, float hy)
		{
			VertexCount = 4;
			Vertices[0].Set(-hx, -hy);
			Vertices[1].Set(hx, -hy);
			Vertices[2].Set(hx, hy);
			Vertices[3].Set(-hx, hy);
		}


		/// <summary>
		/// Build vertices to represent an oriented box.
		/// </summary>
		/// <param name="hx">The half-width</param>
		/// <param name="hy">The half-height.</param>
		/// <param name="center">The center of the box in local coordinates.</param>
		/// <param name="angle">The rotation of the box in local coordinates.</param>
		public void SetAsBox(float hx, float hy, Vector2 center, float angle)
		{
			SetAsBox(hx, hy);
			XForm xf = new XForm();
			xf.Position = center;
			xf.R.Set(angle);

			for (int i = 0; i < vertexCount; ++i)
			{
				Vertices[i] = Common.Math.Mul(xf, Vertices[i]);
			}
		}
	}

	/// <summary>
	/// A convex polygon.
	/// </summary>
	public class PolygonShape : Shape
	{
		// Local position of the polygon centroid.
		Vector2 _centroid;

		OBB _obb;
		/// <summary>
		/// Get the oriented bounding box relative to the parent body.
		/// </summary>
		public OBB OBB { get { return _obb; } }

		Vector2[] _vertices = new Vector2[Settings.MaxPolygonVertices];
		/// <summary>
		/// Get the vertices in local coordinates.
		/// </summary>
		public Vector2[] Vertices { get { return _vertices; } }

		Vector2[] _normals = new Vector2[Settings.MaxPolygonVertices];

		Vector2[] _coreVertices = new Vector2[Settings.MaxPolygonVertices];
		/// <summary>
		/// Get the core vertices in local coordinates. These vertices
		/// represent a smaller polygon that is used for time of impact
		/// computations.
		/// </summary>
		public Vector2[] CoreVertices { get { return _coreVertices; } }
		
		int _vertexCount;
		/// <summary>
		/// Get the vertex count.
		/// </summary>
		public int VertexCount { get { return _vertexCount; } }

		public PolygonShape(ShapeDef def)
			: base(def)
		{
			Box2DXDebug.Assert(def.Type == ShapeType.PolygonShape);
			Type = ShapeType.PolygonShape;
			PolygonDef poly = (PolygonDef)def;

			// Get the vertices transformed into the body frame.
			_vertexCount = poly.VertexCount;
			Box2DXDebug.Assert(3 <= _vertexCount && _vertexCount <= Settings.MaxPolygonVertices);

			// Copy vertices.
			for (int i = 0; i < _vertexCount; ++i)
			{
				_vertices[i] = poly.Vertices[i];
			}

			// Compute normals. Ensure the edges have non-zero length.
			for (int i = 0; i < _vertexCount; ++i)
			{
				int i1 = i;
				int i2 = i + 1 < _vertexCount ? i + 1 : 0;
				Vector2 edge = _vertices[i2] - _vertices[i1];
				Box2DXDebug.Assert(edge.LengthSquared() > Common.Math.FLT_EPSILON * Common.Math.FLT_EPSILON);
				_normals[i] = Vector2.Cross(edge, 1.0f);
				_normals[i].Normalize();
			}

#if DEBUG
			// Ensure the polygon is convex.
			for (int i = 0; i < _vertexCount; ++i)
			{
				for (int j = 0; j < _vertexCount; ++j)
				{
					// Don't check vertices on the current edge.
					if (j == i || j == (i + 1) % _vertexCount)
					{
						continue;
					}
					
					// Your polygon is non-convex (it has an indentation).
					// Or your polygon is too skinny.
					float s = Vector2.Dot(_normals[i], _vertices[j] - _vertices[i]);
					Box2DXDebug.Assert(s < -Settings.LinearSlop);
				}
			}

			// Ensure the polygon is counter-clockwise.
			for (int i = 1; i < _vertexCount; ++i)
			{
				float cross = Vector2.Cross(_normals[i-1], _normals[i]);

				// Keep asinf happy.
				cross = Vector2.Clamp(cross, -1.0f, 1.0f);

				// You have consecutive edges that are almost parallel on your polygon.
				float angle = (float)System.Math.Asin(cross);
				Box2DXDebug.Assert(angle > Settings.AngularSlop);
			}
#endif

			// Compute the polygon centroid.
			_centroid = ComputeCentroid(poly.Vertices, poly.VertexCount);

			// Compute the oriented bounding box.
			ComputeOBB(out _obb, _vertices, _vertexCount);

			// Create core polygon shape by shifting edges inward.
			// Also compute the min/max radius for CCD.
			for (int i = 0; i < _vertexCount; ++i)
			{
				int i1 = i - 1 >= 0 ? i - 1 : _vertexCount - 1;
				int i2 = i;

				Vector2 n1 = _normals[i1];
				Vector2 n2 = _normals[i2];
				Vector2 v = _vertices[i] - _centroid; ;

				Vector2 d = new Vector2();
				d.X = Vector2.Dot(n1, v) - Settings.ToiSlop;
				d.Y = Vector2.Dot(n2, v) - Settings.ToiSlop;

				// Shifting the edge inward by b2_toiSlop should
				// not cause the plane to pass the centroid.

				// Your shape has a radius/extent less than b2_toiSlop.
				Box2DXDebug.Assert(d.X >= 0.0f);
				Box2DXDebug.Assert(d.Y >= 0.0f);
				Mat22 A = new Mat22();
				A.Col1.X = n1.x; A.Col2.X = n1.Y;
				A.Col1.Y = n2.x; A.Col2.Y = n2.Y;
				_coreVertices[i] = A.Solve(d) + _centroid;
			}
		}

		public void UpdateSweepRadius(Vector2 center)
		{
			// Update the sweep radius (maximum radius) as measured from
			// a local center point.
			_sweepRadius = 0.0f;
			for (int i = 0; i < _vertexCount; ++i)
			{
				Vector2 d = _coreVertices[i] - center;
				_sweepRadius = Common.Math.Max(_sweepRadius, d.Length());
			}
		}

		public Vector2 GetFirstVertex(XForm xf)
		{
			return Common.Math.Mul(xf, _coreVertices[0]);
		}

		public Vector2 Centroid(XForm xf)
		{
			return Common.Math.Mul(xf, _centroid);
		}

		public Vector2 Support(XForm xf, Vector2 d)
		{
			Vector2 dLocal = Common.Math.MulT(xf.R, d);

			int bestIndex = 0;
			float bestValue = Vector2.Dot(_coreVertices[0], dLocal);
			for (int i = 1; i < _vertexCount; ++i)
			{
				float value = Vector2.Dot(_coreVertices[i], dLocal);
				if (value > bestValue)
				{
					bestIndex = i;
					bestValue = value;
				}
			}

			return Common.Math.Mul(xf, _coreVertices[bestIndex]);
		}

		public override bool TestPoint(XForm xf, Vector2 p)
		{
			Vector2 pLocal = Common.Math.MulT(xf.R, p - xf.Position);

			for (int i = 0; i < _vertexCount; ++i)
			{
				float dot = Vector2.Dot(_normals[i], pLocal - _vertices[i]);
				if (dot > 0.0f)
				{
					return false;
				}
			}

			return true;
		}

		public override bool TestSegment(XForm xf, out float lambda, out Vector2 normal, Segment segment, float maxLambda)
		{
			float lower = 0.0f, upper = maxLambda;

			Vector2 p1 = Common.Math.MulT(xf.R, segment.P1 - xf.Position);
			Vector2 p2 = Common.Math.MulT(xf.R, segment.P2 - xf.Position);
			Vector2 d = p2 - p1;
			int index = -1;

			for (int i = 0; i < _vertexCount; ++i)
			{
				// p = p1 + a * d
				// dot(normal, p - v) = 0
				// dot(normal, p1 - v) + a * dot(normal, d) = 0
				float numerator = Vector2.Dot(_normals[i], _vertices[i] - p1);
				float denominator = Vector2.Dot(_normals[i], d);

				if (denominator < 0.0f && numerator > lower * denominator)
				{
					// The segment enters this half-space.
					lower = numerator / denominator;
					index = i;
				}
				else if (denominator > 0.0f && numerator < upper * denominator)
				{
					// The segment exits this half-space.
					upper = numerator / denominator;
				}

				if (upper < lower)
				{
					return false;
				}
			}

			Box2DXDebug.Assert(0.0f <= lower && lower <= maxLambda);

			if (index >= 0)
			{
				lambda = lower;
				normal = Common.Math.Mul(xf.R, _normals[index]);
				return true;
			}

			return false;
		}

		public override void ComputeAABB(out AABB aabb, XForm xf)
		{
			Mat22 R = Common.Math.Mul(xf.R, _obb.R);
			Mat22 absR = Common.Math.Abs(R);
			Vector2 h = Common.Math.Mul(absR, _obb.Extents);
			Vector2 position = xf.Position + Common.Math.Mul(xf.R, _obb.Center);
			aabb.LowerBound = position - h;
			aabb.UpperBound = position + h;
		}

		public override void ComputeSweptAABB(out AABB aabb, XForm xf1, XForm xf2)
		{
			AABB aabb1, aabb2;
			ComputeAABB(out aabb1, transform1);
			ComputeAABB(out aabb2, transform2);
			aabb.LowerBound = Common.Math.Min(aabb1.LowerBound, aabb2.LowerBound);
			aabb.UpperBound = Common.Math.Max(aabb1.UpperBound, aabb2.UpperBound);
		}

		public override void ComputeMass(out MassData massData)
		{
			// Polygon mass, centroid, and inertia.
			// Let rho be the polygon density in mass per unit area.
			// Then:
			// mass = rho * int(dA)
			// centroid.x = (1/mass) * rho * int(x * dA)
			// centroid.y = (1/mass) * rho * int(y * dA)
			// I = rho * int((x*x + y*y) * dA)
			//
			// We can compute these integrals by summing all the integrals
			// for each triangle of the polygon. To evaluate the integral
			// for a single triangle, we make a change of variables to
			// the (u,v) coordinates of the triangle:
			// x = x0 + e1x * u + e2x * v
			// y = y0 + e1y * u + e2y * v
			// where 0 <= u && 0 <= v && u + v <= 1.
			//
			// We integrate u from [0,1-v] and then v from [0,1].
			// We also need to use the Jacobian of the transformation:
			// D = cross(e1, e2)
			//
			// Simplification: triangle centroid = (1/3) * (p1 + p2 + p3)
			//
			// The rest of the derivation is handled by computer algebra.
			
			Box2DXDebug.Assert(_vertexCount >= 3);

			Vector2 center = new Vector2(); 
			center.Set(0.0f, 0.0f);
			float area = 0.0f;
			float I = 0.0f;

			// pRef is the reference point for forming triangles.
			// It's location doesn't change the result (except for rounding error).
			Vector2 pRef = new Vector2(0.0f, 0.0f);

#if O
			// This code would put the reference point inside the polygon.
			for (int i = 0; i < _vertexCount; ++i)
			{
				pRef += _vertices[i];
			}
			pRef *= 1.0f / count;
#endif

			float k_inv3 = 1.0f / 3.0f;

			for (int i = 0; i < _vertexCount; ++i)
			{
				// Triangle vertices.
				Vector2 p1 = pRef;
				Vector2 p2 = _vertices[i];
				Vector2 p3 = i + 1 < _vertexCount ? _vertices[i + 1] : _vertices[0];

				Vector2 e1 = p2 - p1;
				Vector2 e2 = p3 - p1;

				float D = Vector2.Cross(e1, e2);

				float triangleArea = 0.5f * D;
				area += triangleArea;

				// Area weighted centroid
				center += triangleArea * k_inv3 * (p1 + p2 + p3);

				float px = p1.X, py = p1.Y;
				float ex1 = e1.X, ey1 = e1.Y;
				float ex2 = e2.X, ey2 = e2.Y;

				float intx2 = k_inv3 * (0.25f * (ex1 * ex1 + ex2 * ex1 + ex2 * ex2) + (px * ex1 + px * ex2)) + 0.5f * px * px;
				float inty2 = k_inv3 * (0.25f * (ey1 * ey1 + ey2 * ey1 + ey2 * ey2) + (py * ey1 + py * ey2)) + 0.5f * py * py;

				I += D * (intx2 + inty2);
			}

			// Total mass
			massData.Mass = _density * area;
			
			// Center of mass
			Box2DXDebug.Assert(area > Common.Math.FLT_EPSILON);
			center *= 1.0f / area;
			massData.Center = center;

			// Inertia tensor relative to the local origin.
			massData.I = _density * I;
		}

		/// <summary>
		/// Get local centroid relative to the parent body.
		/// </summary>
		/// <returns></returns>
		public Vector2 GetCentroid() { return _centroid; }
	}
}