using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Linq;
using RobTeach.Models;
using RobTeach.Utils; // For AppLogger

namespace RobTeach.Services
{
    public class CadService
    {
        public DxfFile LoadDxf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("DXF file not found.", filePath);
            }
            
            try
            {
                DxfFile dxf = DxfFile.Load(filePath);
                return dxf;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading or parsing DXF file: {ex.Message}", ex);
            }
        }
        
        public List<System.Windows.Shapes.Shape> GetWpfShapesFromDxf(DxfFile dxfFile)
        {
            var wpfShapes = new List<System.Windows.Shapes.Shape>();
            if (dxfFile == null)
            {
                AppLogger.Log("[CadService] GetWpfShapesFromDxf: dxfFile is null. Returning empty list.", LogLevel.Warning);
                return wpfShapes;
            }
            AppLogger.Log($"[CadService] GetWpfShapesFromDxf: Processing {dxfFile.Entities.Count()} entities from DXF document.", LogLevel.Debug);
            int entityCounter = 0;

            foreach (DxfEntity entity in dxfFile.Entities) // Ensure entity is typed as DxfEntity for direct Handle access
            {
                System.Windows.Shapes.Shape? wpfShape = null;
                // uint entityHandle = entity.Handle; // Access handle safely // Removed due to Handle not being available directly
                AppLogger.Log($"[CadService] GetWpfShapesFromDxf: Processing entity at index {entityCounter} (C# Type: {entity.GetType().Name}, Layer: {entity.Layer}).", LogLevel.Debug);

                switch (entity)
                {
                    case DxfLine dxfLine:
                        wpfShape = new System.Windows.Shapes.Line
                        {
                            X1 = dxfLine.P1.X, Y1 = dxfLine.P1.Y,
                            X2 = dxfLine.P2.X, Y2 = dxfLine.P2.Y,
                            IsHitTestVisible = true
                        };
                        AppLogger.Log($"[CadService]   Converted DxfLine to WPF Line.", LogLevel.Debug);
                        break;

                    case DxfArc dxfArc:
                        wpfShape = CreateArcPath(dxfArc);
                        if (wpfShape != null)
                            AppLogger.Log($"[CadService]   Converted DxfArc to WPF Path.", LogLevel.Debug);
                        else
                            AppLogger.Log($"[CadService]   FAILED to convert DxfArc to WPF Path.", LogLevel.Warning);
                        break;

                    case DxfCircle dxfCircle:
                        var ellipseGeometry = new EllipseGeometry(
                            new System.Windows.Point(dxfCircle.Center.X, dxfCircle.Center.Y),
                            dxfCircle.Radius,
                            dxfCircle.Radius
                        );
                        wpfShape = new System.Windows.Shapes.Path
                        {
                            Data = ellipseGeometry,
                            Fill = Brushes.Transparent,
                            IsHitTestVisible = true
                        };
                        AppLogger.Log($"[CadService]   Converted DxfCircle to WPF Path (EllipseGeometry).", LogLevel.Debug);
                        break;
                    case DxfLwPolyline lwPoly:
                        wpfShape = ConvertLwPolylineToWpfPath(lwPoly);
                        if(wpfShape != null)
                            AppLogger.Log($"[CadService]   Converted DxfLwPolyline to WPF Path.", LogLevel.Debug);
                        else
                            AppLogger.Log($"[CadService]   FAILED to convert DxfLwPolyline to WPF Path (returned null).", LogLevel.Warning);
                        break;
                    default:
                        AppLogger.Log($"[CadService]   EntityType '{entity.GetType().Name}' not explicitly supported for WPF shape conversion. Entity skipped.", LogLevel.Debug);
                        break;
                }
                wpfShapes.Add(wpfShape); // Add null if not converted, to maintain list correspondence
                entityCounter++;
            }
            AppLogger.Log($"[CadService] GetWpfShapesFromDxf: Finished processing. Returning list with {wpfShapes.Count} elements.", LogLevel.Debug);
            return wpfShapes;
        }

        private System.Windows.Shapes.Path? CreateArcPath(DxfArc dxfArc)
        {
            if (dxfArc == null) {
                AppLogger.Log("[CadService] CreateArcPath: Input DxfArc is null.", LogLevel.Warning);
                return null;
            }
            try
            {
                double startAngleRad = dxfArc.StartAngle * Math.PI / 180.0;
                double endAngleRad = dxfArc.EndAngle * Math.PI / 180.0;
                
                double arcStartX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(startAngleRad);
                double arcStartY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(startAngleRad);
                var pathStartPoint = new System.Windows.Point(arcStartX, arcStartY);

                double arcEndX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(endAngleRad);
                double arcEndY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(endAngleRad);
                var arcSegmentEndPoint = new System.Windows.Point(arcEndX, arcEndY);

                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepAngleDegrees < 0) sweepAngleDegrees += 360;
                // Ensure sweep is not exactly 0 or 360 if start and end are same, which can happen for full circles passed as arcs
                if (sweepAngleDegrees == 0 && dxfArc.StartAngle != dxfArc.EndAngle) sweepAngleDegrees = 360;
                if (sweepAngleDegrees == 360 && dxfArc.StartAngle == dxfArc.EndAngle) sweepAngleDegrees = 360;


                bool isLargeArc = sweepAngleDegrees > 180.0;
                SweepDirection sweepDirection = SweepDirection.Counterclockwise; // DXF arcs are CCW by convention

                ArcSegment arcSegment = new ArcSegment
                {
                    Point = arcSegmentEndPoint,
                    Size = new System.Windows.Size(dxfArc.Radius, dxfArc.Radius),
                    IsLargeArc = isLargeArc,
                    SweepDirection = sweepDirection,
                    RotationAngle = 0, // DXF Arcs are circular, no rotation of ellipse axes
                    IsStroked = true
                };

                PathFigure pathFigure = new PathFigure
                {
                    StartPoint = pathStartPoint,
                    IsClosed = false // Arcs are not closed paths by definition
                };
                pathFigure.Segments.Add(arcSegment);

                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                return new System.Windows.Shapes.Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
                };
            }
            catch(Exception ex)
            {
                // uint h = dxfArc?.Handle ?? 0; // Safely access handle // Removed due to Handle not being available directly
                AppLogger.Log($"[CadService] CreateArcPath: Error converting DxfArc (Type: {dxfArc?.GetType().Name}, Layer: {dxfArc?.Layer}): {ex.Message}", ex, LogLevel.Error);
                return null;
            }
        }

    private System.Windows.Shapes.Path? ConvertLwPolylineToWpfPath(DxfLwPolyline lwPolyline)
    {
        // uint polylineHandle = lwPolyline.Handle; // Get handle once // Removed due to Handle not being available directly
        if (lwPolyline.Vertices.Count == 0)
        {
            AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: LwPolyline (Layer: {lwPolyline.Layer}) has no vertices, returning null.", LogLevel.Debug);
            return null;
        }

        AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath: Processing LwPolyline (Layer: {lwPolyline.Layer}) with {lwPolyline.Vertices.Count} vertices. IsClosed: {lwPolyline.IsClosed}", LogLevel.Debug);

        PathGeometry pathGeometry = new PathGeometry();
        PathFigure pathFigure = new PathFigure();

        var firstVertex = lwPolyline.Vertices.First();
        pathFigure.StartPoint = new Point(firstVertex.X, firstVertex.Y);
        AppLogger.Log($"[CadService] LwPolyline PathFigure StartPoint: ({firstVertex.X:F3}, {firstVertex.Y:F3})", LogLevel.Debug);

        for (int i = 0; i < lwPolyline.Vertices.Count; i++)
        {
            var v1 = lwPolyline.Vertices[i];
            DxfLwPolylineVertex v2;
            string segmentTypeForLog;

            bool isLastVertex = (i == lwPolyline.Vertices.Count - 1);

            if (!isLastVertex)
            {
                v2 = lwPolyline.Vertices[i + 1];
                segmentTypeForLog = "Segment";
            }
            else if (lwPolyline.IsClosed)
            {
                v2 = lwPolyline.Vertices.First();
                segmentTypeForLog = "Closing Segment";
            }
            else // Last vertex of an open polyline
            {
                 AppLogger.Log($"[CadService] LwPolyline open, end of vertices at index {i}. No further segments from this vertex.", LogLevel.Debug);
                 break;
            }

            Point p1Wpf = new Point(v1.X, v1.Y);
            Point p2Wpf = new Point(v2.X, v2.Y);
            AppLogger.Log($"[CadService] LwPolyline {segmentTypeForLog} {i}: V1=({v1.X:F3},{v1.Y:F3} B={v1.Bulge:F4}) to V2=({v2.X:F3},{v2.Y:F3})", LogLevel.Debug);

            // If current point and next point are the same, skip creating a zero-length segment, unless it's a bulge defining a full circle
            if (p1Wpf == p2Wpf && Math.Abs(v1.Bulge) < 1e-6) { // Bulge for full circle would be 1 or -1 for specific cases, or non-zero for arcs.
                 AppLogger.Log($"[CadService] LwPolyline SKIPPED zero-length straight segment from ({p1Wpf.X:F3},{p1Wpf.Y:F3}) to ({p2Wpf.X:F3},{p2Wpf.Y:F3}).", LogLevel.Debug);
                // If this is the last segment of a closed polyline and it's zero length, PathFigure.IsClosed will handle it.
                // If it's an intermediate zero-length segment, skipping is fine.
                if (isLastVertex && lwPolyline.IsClosed) { /* Let IsClosed handle this */ }
                else continue;
            }

            if (Math.Abs(v1.Bulge) < 1e-6)
            {
                pathFigure.Segments.Add(new LineSegment(p2Wpf, true));
                AppLogger.Log($"[CadService] LwPolyline Added LineSegment to ({p2Wpf.X:F3}, {p2Wpf.Y:F3})", LogLevel.Debug);
            }
            else
            {
                var arcSegment = CalculateArcSegmentFromBulge(p1Wpf, p2Wpf, v1.Bulge);
                if (arcSegment != null) {
                    pathFigure.Segments.Add(arcSegment);
                    AppLogger.Log($"[CadService] LwPolyline Added ArcSegment: EndPoint=({arcSegment.Point.X:F3},{arcSegment.Point.Y:F3}), Size=({arcSegment.Size.Width:F3},{arcSegment.Size.Height:F3}), IsLargeArc={arcSegment.IsLargeArc}, Sweep={arcSegment.SweepDirection}", LogLevel.Debug);
                } else {
                    AppLogger.Log($"[CadService] LwPolyline FAILED to calculate ArcSegment from V1=({v1.X:F3},{v1.Y:F3} B={v1.Bulge:F4}) to V2=({v2.X:F3},{v2.Y:F3}), adding LineSegment instead.", LogLevel.Warning);
                    pathFigure.Segments.Add(new LineSegment(p2Wpf, true)); // Fallback to line segment
                }
            }
        }

        if (lwPolyline.Vertices.Count == 1) {
             // A single vertex LwPolyline. PathFigure has StartPoint. To make it "visible" for hit-testing or as a tiny dot.
             // We can add a zero-length line segment. Or do nothing if single points are not meant to be selectable shapes.
             // For now, let it be, PathFigure.Segments might be empty. Add if needed.
             AppLogger.Log($"[CadService] LwPolyline has only one vertex at ({pathFigure.StartPoint.X:F3}, {pathFigure.StartPoint.Y:F3}). PathFigure segments count: {pathFigure.Segments.Count}", LogLevel.Debug);
        }

        pathFigure.IsClosed = lwPolyline.IsClosed;
        AppLogger.Log($"[CadService] LwPolyline PathFigure IsClosed set to: {pathFigure.IsClosed}", LogLevel.Debug);

        if (pathFigure.StartPoint == null && !pathFigure.Segments.Any()) {
             AppLogger.Log($"[CadService] LwPolyline (Layer: {lwPolyline.Layer}) resulted in an empty PathFigure. Returning null.", LogLevel.Warning);
            return null; // Avoid creating Path with empty Figure/Geometry
        }

        pathGeometry.Figures.Add(pathFigure);

        AppLogger.Log($"[CadService] ConvertLwPolylineToWpfPath for LwPolyline (Layer: {lwPolyline.Layer}) completed.", LogLevel.Debug);
        return new System.Windows.Shapes.Path
        {
            Data = pathGeometry,
            Fill = Brushes.Transparent,
            IsHitTestVisible = true
        };
    }

    private ArcSegment? CalculateArcSegmentFromBulge(Point p1, Point p2, double bulge)
    {
        AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: P1=({p1.X:F3},{p1.Y:F3}), P2=({p2.X:F3},{p2.Y:F3}), Bulge={bulge:F4}", LogLevel.Debug);

        // theta is the included angle of the arc segment.
        double theta = 4 * Math.Atan(bulge);

        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double chord = Math.Sqrt(dx * dx + dy * dy);

        if (Math.Abs(chord) < 1e-9)
        {
            // Points are coincident. An arc segment isn't well-defined.
            // Depending on bulge, this could be a full circle if bulge is +/-1, but DXF LwPolyline spec implies segment between vertices.
            AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Chord length ({chord:E3}) near zero. Bulge is {bulge:F4}. Returning null for ArcSegment.", LogLevel.Debug);
            return null; // Or treat as a point / zero-length line if necessary.
        }

        double radius;
        // If sin(theta/2) is zero, it means theta is 0 or 2*PI.
        // theta = 0 implies bulge = 0, which should be handled as a line segment.
        // theta = 2*PI (or multiples) means a full circle segment if p1 and p2 are the same, which is caught by chord length check.
        // If p1 and p2 are different, theta cannot be 2*PI for a single segment.
        double sinHalfTheta = Math.Sin(theta / 2.0);
        if (Math.Abs(sinHalfTheta) < 1e-9)
        {
             // This case (bulge != 0 but sin(theta/2) == 0) implies theta is a multiple of 2*PI.
             // For a segment between two distinct points, this is geometrically problematic for arc radius.
             // It suggests an issue with bulge value or an extreme case.
             AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Sin(theta/2) near zero (theta={theta*180/Math.PI:F2}deg). Cannot form proper arc. Chord={chord:F3}.", LogLevel.Warning);
             return null; // Cannot form a valid arc segment here.
        } else {
             radius = Math.Abs(chord / (2 * sinHalfTheta));
        }

        SweepDirection sweepDirection = (bulge > 0) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
        bool isLargeArc = Math.Abs(theta) > Math.PI;
        Size arcSize = new Size(radius, radius);

        AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Result: Theta={theta * 180.0/Math.PI:F3}deg, Chord={chord:F3}, Radius={radius:F3}, IsLargeArc={isLargeArc}, Sweep={sweepDirection}", LogLevel.Debug);

        if (double.IsInfinity(radius) || double.IsNaN(radius) || radius > 1e9) // Check for extreme radius
        {
            AppLogger.Log($"[CadService] CalculateArcSegmentFromBulge: Radius is extreme ({radius:E3}). May fall back to line segment.", LogLevel.Warning);
            // It might be better to return null and let caller draw a line segment.
            return null;
        }

        return new ArcSegment(p2, arcSize, 0, isLargeArc, sweepDirection, true);
    }

        // Method stubs for trajectory point conversion - to be reviewed/completed if needed by other parts
        public List<System.Windows.Point> ConvertLineToPoints(DxfLine line)
        {
            var points = new List<System.Windows.Point>();
            if (line == null) return points;
            points.Add(new System.Windows.Point(line.P1.X, line.P1.Y));
            points.Add(new System.Windows.Point(line.P2.X, line.P2.Y));
            return points;
        }

        public List<System.Windows.Point> ConvertArcToPoints(DxfArc arc, double resolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            if (arc == null || resolutionDegrees <= 0) return points;
            // ... (implementation as before) ...
            double startAngle = arc.StartAngle;
            double endAngle = arc.EndAngle;
            double radius = arc.Radius;
            System.Windows.Point center = new System.Windows.Point(arc.Center.X, arc.Center.Y);
            if (endAngle < startAngle) endAngle += 360;
            double currentAngle = startAngle;
            while (currentAngle <= endAngle)
            {
                double radAngle = currentAngle * Math.PI / 180.0;
                double x = center.X + radius * Math.Cos(radAngle);
                double y = center.Y + radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
                currentAngle += resolutionDegrees;
            }
            if (Math.Abs(currentAngle - resolutionDegrees - endAngle) > 0.001)
            {
                double endRadAngle = endAngle * Math.PI / 180.0;
                points.Add(new System.Windows.Point(center.X + radius * Math.Cos(endRadAngle), center.Y + radius * Math.Sin(endRadAngle)));
            }
            return points;
        }

        public List<System.Windows.Point> ConvertCircleToPoints(DxfCircle circle, double resolutionDegrees)
        {
            List<System.Windows.Point> points = new List<System.Windows.Point>();
            if (circle == null || resolutionDegrees <= 0) return points;
            for (double angle = 0; angle < 360.0; angle += resolutionDegrees)
            {
                double radAngle = angle * Math.PI / 180.0;
                double x = circle.Center.X + circle.Radius * Math.Cos(radAngle);
                double y = circle.Center.Y + circle.Radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
            }
             if (points.Count > 0) points.Add(points[0]); // Close the circle
            return points;
        }
        public List<System.Windows.Point> ConvertLineTrajectoryToPoints(Trajectory trajectory)
        {
            var points = new List<System.Windows.Point>();
            if (trajectory == null || trajectory.PrimitiveType != "Line") return points;
            DxfPoint start = trajectory.LineStartPoint;
            DxfPoint end = trajectory.LineEndPoint;
            if (trajectory.IsReversed) { points.Add(new System.Windows.Point(end.X, end.Y)); points.Add(new System.Windows.Point(start.X, start.Y)); }
            else { points.Add(new System.Windows.Point(start.X, start.Y)); points.Add(new System.Windows.Point(end.X, end.Y)); }
            return points;
        }
        public List<System.Windows.Point> ConvertArcTrajectoryToPoints(Trajectory trajectory, double resolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            // ... (implementation as before, needs robust 3-point to arc param logic if OriginalDxfEntity is not DxfArc) ...
            if (trajectory == null || trajectory.PrimitiveType != "Arc" || resolutionDegrees <= 0) return points;
            if (trajectory.OriginalDxfEntity is DxfArc dxfArc) { /* ... as before ... */
                double startAngle = dxfArc.StartAngle;
                double endAngle = dxfArc.EndAngle;
                if (trajectory.IsReversed) { double temp = startAngle; startAngle = endAngle; endAngle = temp; }
                if (endAngle < startAngle) endAngle += 360.0;
                for (double currentAngle = startAngle; currentAngle <= endAngle; currentAngle += resolutionDegrees) {
                    double rad = currentAngle * Math.PI / 180.0;
                    points.Add(new Point(dxfArc.Center.X + dxfArc.Radius * Math.Cos(rad), dxfArc.Center.Y + dxfArc.Radius * Math.Sin(rad)));
                }
                double finalRad = endAngle * Math.PI / 180.0;
                points.Add(new Point(dxfArc.Center.X + dxfArc.Radius * Math.Cos(finalRad), dxfArc.Center.Y + dxfArc.Radius * Math.Sin(finalRad)));

            } else { AppLogger.Log($"[CadService] ConvertArcTrajectoryToPoints: Trajectory '{trajectory.ToString()}' OriginalDxfEntity is not a DxfArc or is null.", LogLevel.Warning); }
            return points;
        }
        public List<System.Windows.Point> ConvertCircleTrajectoryToPoints(Trajectory trajectory, double resolutionDegrees)
        {
            AppLogger.Log("[CadService] ConvertCircleTrajectoryToPoints called - Note: This method is obsolete for point generation; MainWindow.PopulateTrajectoryPoints should be used.", LogLevel.Debug);
            return new List<System.Windows.Point>(); // Obsolete
        }
    }
}
