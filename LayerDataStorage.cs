/*
This file is part of MatterSlice. A commandline utility for
generating 3D printing GCode.

Copyright (C) 2013 David Braam
Copyright (c) 2014, Lars Brubaker

MatterSlice is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as
published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using ClipperLib;
using System.Collections.Generic;

namespace MatterHackers.MatterSlice
{
	using System.IO;
	using System.Linq;
	using Polygons = List<List<IntPoint>>;

	public class LayerDataStorage
	{
		public List<ExtruderLayers> Extruders = new List<ExtruderLayers>();
		public Point3 modelSize, modelMin, modelMax;
		public Polygons raftOutline = new Polygons();
		public Polygons skirt = new Polygons();
		public NewSupport support = null;
		public IntPoint wipePoint;
		public List<Polygons> wipeShield = new List<Polygons>();
		public Polygons wipeTower = new Polygons();

		public void CreateIslandData()
		{
			for (int extruderIndex = 0; extruderIndex < Extruders.Count; extruderIndex++)
			{
				Extruders[extruderIndex].CreateIslandData();
			}
		}

		public void DumpLayerparts(string filename)
		{
			LayerDataStorage storage = this;
			StreamWriter streamToWriteTo = new StreamWriter(filename);
			streamToWriteTo.Write("<!DOCTYPE html><html><body>");
			Point3 modelSize = storage.modelSize;
			Point3 modelMin = storage.modelMin;

			for (int extruderIndex = 0; extruderIndex < storage.Extruders.Count; extruderIndex++)
			{
				for (int layerNr = 0; layerNr < storage.Extruders[extruderIndex].Layers.Count; layerNr++)
				{
					streamToWriteTo.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" style=\"width: 500px; height:500px\">\n");
					SliceLayer layer = storage.Extruders[extruderIndex].Layers[layerNr];
					for (int i = 0; i < layer.Islands.Count; i++)
					{
						LayerIsland part = layer.Islands[i];
						for (int j = 0; j < part.IslandOutline.Count; j++)
						{
							streamToWriteTo.Write("<polygon points=\"");
							for (int k = 0; k < part.IslandOutline[j].Count; k++)
								streamToWriteTo.Write("{0},{1} ".FormatWith((float)(part.IslandOutline[j][k].X - modelMin.x) / modelSize.x * 500, (float)(part.IslandOutline[j][k].Y - modelMin.y) / modelSize.y * 500));
							if (j == 0)
								streamToWriteTo.Write("\" style=\"fill:gray; stroke:black;stroke-width:1\" />\n");
							else
								streamToWriteTo.Write("\" style=\"fill:red; stroke:black;stroke-width:1\" />\n");
						}
					}
					streamToWriteTo.Write("</svg>\n");
				}
			}
			streamToWriteTo.Write("</body></html>");
			streamToWriteTo.Close();
		}

		public void GenerateRaftOutlines(int extraDistanceAroundPart_um, ConfigSettings config)
		{
			LayerDataStorage storage = this;
			for (int extruderIndex = 0; extruderIndex < storage.Extruders.Count; extruderIndex++)
			{
				if (config.ContinuousSpiralOuterPerimeter && extruderIndex > 0)
				{
					continue;
				}

				if (storage.Extruders[extruderIndex].Layers.Count < 1)
				{
					continue;
				}

				SliceLayer layer = storage.Extruders[extruderIndex].Layers[0];
				// let's find the first layer that has something in it for the raft rather than a zero layer
				if (layer.Islands.Count == 0 && storage.Extruders[extruderIndex].Layers.Count > 2) layer = storage.Extruders[extruderIndex].Layers[1];
				for (int partIndex = 0; partIndex < layer.Islands.Count; partIndex++)
				{
					if (config.ContinuousSpiralOuterPerimeter && partIndex > 0)
					{
						continue;
					}

					storage.raftOutline = storage.raftOutline.CreateUnion(layer.Islands[partIndex].IslandOutline.Offset(extraDistanceAroundPart_um));
				}
			}

			storage.raftOutline = storage.raftOutline.CreateUnion(storage.wipeTower.Offset(extraDistanceAroundPart_um));
			if (storage.support != null)
			{
				storage.raftOutline = storage.raftOutline.CreateUnion(storage.support.GetBedOutlines().Offset(extraDistanceAroundPart_um));
			}
		}

		public void GenerateSkirt(int distance, int extrusionWidth_um, int numberOfLoops, int brimCount, int minLength, int initialLayerHeight, ConfigSettings config)
		{
			LayerDataStorage storage = this;
			bool externalOnly = (distance > 0);

			List<Polygons> skirtLoops = new List<Polygons>();

			Polygons skirtPolygons = GetSkirtBounds(config, storage, externalOnly, distance, extrusionWidth_um, brimCount);

			// Find convex hull for the skirt outline 
			Polygons convextHull = new Polygons(new[] { skirtPolygons.CreateConvexHull() });

			// Create skirt loops from the ConvexHull 
			for (int skirtLoop = 0; skirtLoop < numberOfLoops; skirtLoop++)
			{
				int offsetDistance = distance + extrusionWidth_um * skirtLoop + extrusionWidth_um / 2;

				storage.skirt.AddAll(convextHull.Offset(offsetDistance));

				int length = (int)storage.skirt.PolygonLength();
				if (skirtLoop + 1 >= numberOfLoops && length > 0 && length < minLength)
				{
					// add more loops for as long as we have not extruded enough length
					numberOfLoops++;
				}
			}
		}

		private static Polygons GetSkirtBounds(ConfigSettings config, LayerDataStorage storage, bool externalOnly, int distance, int extrusionWidth_um, int brimCount)
		{
			Polygons allOutlines = new Polygons();

			// Loop over every extruder
			for (int extrudeIndex = 0; extrudeIndex < storage.Extruders.Count; extrudeIndex++)
			{
				// Only process the first extruder on spiral vase or 
				// skip extruders that have empty layers
				if (config.ContinuousSpiralOuterPerimeter)
				{
					SliceLayer layer0 = storage.Extruders[extrudeIndex].Layers[0];
					allOutlines.AddAll(layer0.Islands[0]?.IslandOutline);

					break;
				}

				// Add the layers outline to allOutlines
				SliceLayer layer = storage.Extruders[extrudeIndex].Layers[0];
				allOutlines.AddAll(layer.AllOutlines);
			}

			bool hasWipeTower = storage.wipeTower.PolygonLength() > 0;

			Polygons skirtPolygons = hasWipeTower ? new Polygons(storage.wipeTower) : new Polygons();

			if (brimCount > 0)
			{
				Polygons brimLoops = new Polygons();

				// Loop over the requested brimCount creating and unioning a new perimeter for each island
				for (int brimIndex = 0; brimIndex < brimCount; brimIndex++)
				{
					int offsetDistance = extrusionWidth_um * brimIndex + extrusionWidth_um / 2;

					Polygons unionedIslandOutlines = new Polygons();

					// Grow each island by the current brim distance
					foreach(var island in allOutlines)
					{
						var polygons = new Polygons();
						polygons.Add(island);

						// Union the island brims
						unionedIslandOutlines = unionedIslandOutlines.CreateUnion(polygons.Offset(offsetDistance));
					}

					// Extend the polygons to account for the brim (ensures convex hull takes this data into account) 
					brimLoops.AddAll(unionedIslandOutlines);
				}

				// TODO: This is a quick hack, reuse the skirt data to stuff in the brim. Good enough from proof of concept
				storage.skirt.AddAll(brimLoops);

				skirtPolygons = skirtPolygons.CreateUnion(brimLoops);
			}
			else
			{
				skirtPolygons = skirtPolygons.CreateUnion(allOutlines);
			}

			if (storage.support != null)
			{
				skirtPolygons = skirtPolygons.CreateUnion(storage.support.GetBedOutlines());
			}

			return skirtPolygons;
		}

		public void WriteRaftGCodeIfRequired(ConfigSettings config, GCodeExport gcode)
		{
			LayerDataStorage storage = this;
			if (config.ShouldGenerateRaft())
			{
				GCodePathConfig raftBaseConfig = new GCodePathConfig(config.FirstLayerSpeed, config.RaftBaseExtrusionWidth_um, "SUPPORT");
				GCodePathConfig raftMiddleConfig = new GCodePathConfig(config.RaftPrintSpeed, config.RaftInterfaceExtrusionWidth_um, "SUPPORT");
				GCodePathConfig raftSurfaceConfig = new GCodePathConfig((config.RaftSurfacePrintSpeed > 0) ? config.RaftSurfacePrintSpeed : config.RaftPrintSpeed, config.RaftSurfaceExtrusionWidth_um, "SUPPORT");

				// create the raft base
				{
					gcode.WriteComment("LAYER:-3");
					gcode.WriteComment("RAFT BASE");
					GCodePlanner gcodeLayer = new GCodePlanner(gcode, config.TravelSpeed, config.MinimumTravelToCauseRetraction_um);
					if (config.RaftExtruder >= 0)
					{
						// if we have a specified raft extruder use it
						gcodeLayer.SetExtruder(config.RaftExtruder);
					}
					else if (config.SupportExtruder >= 0)
					{
						// else preserve the old behavior of using the support extruder if set.
						gcodeLayer.SetExtruder(config.SupportExtruder);
					}

					gcode.setZ(config.RaftBaseThickness_um);
					gcode.SetExtrusion(config.RaftBaseThickness_um, config.FilamentDiameter_um, config.ExtrusionMultiplier);

					Polygons raftLines = new Polygons();
					Infill.GenerateLinePaths(storage.raftOutline, ref raftLines, config.RaftBaseLineSpacing_um, config.InfillExtendIntoPerimeter_um, 0);

					// write the skirt around the raft
					gcodeLayer.QueuePolygonsByOptimizer(storage.skirt, raftBaseConfig);

					// write the outline of the raft
					gcodeLayer.QueuePolygonsByOptimizer(storage.raftOutline, raftBaseConfig);

					// write the inside of the raft base
					gcodeLayer.QueuePolygonsByOptimizer(raftLines, raftBaseConfig);

					gcodeLayer.WriteQueuedGCode(config.RaftBaseThickness_um);
				}

				if (config.RaftFanSpeedPercent > 0)
				{
					gcode.WriteFanCommand(config.RaftFanSpeedPercent);
				}

				// raft middle layers
				{
					gcode.WriteComment("LAYER:-2");
					gcode.WriteComment("RAFT MIDDLE");
					GCodePlanner gcodeLayer = new GCodePlanner(gcode, config.TravelSpeed, config.MinimumTravelToCauseRetraction_um);
					gcode.setZ(config.RaftBaseThickness_um + config.RaftInterfaceThicknes_um);
					gcode.SetExtrusion(config.RaftInterfaceThicknes_um, config.FilamentDiameter_um, config.ExtrusionMultiplier);

					Polygons raftLines = new Polygons();
					Infill.GenerateLinePaths(storage.raftOutline, ref raftLines, config.RaftInterfaceLineSpacing_um, config.InfillExtendIntoPerimeter_um, 45);
					gcodeLayer.QueuePolygonsByOptimizer(raftLines, raftMiddleConfig);

					gcodeLayer.WriteQueuedGCode(config.RaftInterfaceThicknes_um);
				}

				for (int raftSurfaceIndex = 1; raftSurfaceIndex <= config.RaftSurfaceLayers; raftSurfaceIndex++)
				{
					gcode.WriteComment("LAYER:-1");
					gcode.WriteComment("RAFT SURFACE");
					GCodePlanner gcodeLayer = new GCodePlanner(gcode, config.TravelSpeed, config.MinimumTravelToCauseRetraction_um);
					gcode.setZ(config.RaftBaseThickness_um + config.RaftInterfaceThicknes_um + config.RaftSurfaceThickness_um * raftSurfaceIndex);
					gcode.SetExtrusion(config.RaftSurfaceThickness_um, config.FilamentDiameter_um, config.ExtrusionMultiplier);

					Polygons raftLines = new Polygons();
					if (raftSurfaceIndex == config.RaftSurfaceLayers)
					{
						// make sure the top layer of the raft is 90 degrees offset to the first layer of the part so that it has minimum contact points.
						Infill.GenerateLinePaths(storage.raftOutline, ref raftLines, config.RaftSurfaceLineSpacing_um, config.InfillExtendIntoPerimeter_um, config.InfillStartingAngle + 90);
					}
					else
					{
						Infill.GenerateLinePaths(storage.raftOutline, ref raftLines, config.RaftSurfaceLineSpacing_um, config.InfillExtendIntoPerimeter_um, 90 * raftSurfaceIndex);
					}
					gcodeLayer.QueuePolygonsByOptimizer(raftLines, raftSurfaceConfig);

					gcodeLayer.WriteQueuedGCode(config.RaftInterfaceThicknes_um);
				}
			}
		}
	}
}