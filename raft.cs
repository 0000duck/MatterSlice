/*
Copyright (C) 2013 David Braam
Copyright (c) 2014, Lars Brubaker

This file is part of MatterSlice.

MatterSlice is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

MatterSlice is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with MatterSlice.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;

using ClipperLib;

namespace MatterHackers.MatterSlice
{
    using Polygon = List<IntPoint>;
    using Polygons = List<List<IntPoint>>;

    public static class Raft
    {
        public static void GenerateRaftOutlines(SliceDataStorage storage, int extraDistanceAroundPart_�m)
        {
            for (int volumeIdx = 0; volumeIdx < storage.volumes.Count; volumeIdx++)
            {
                if (storage.volumes[volumeIdx].layers.Count < 1)
                {
                    continue;
                }

                SliceLayer layer = storage.volumes[volumeIdx].layers[0];
                for (int i = 0; i < layer.parts.Count; i++)
                {
                    storage.raftOutline = storage.raftOutline.CreateUnion(layer.parts[i].outline.Offset(extraDistanceAroundPart_�m));
                }
            }

            SupportPolyGenerator supportGenerator = new SupportPolyGenerator(storage.support, 0);
            storage.raftOutline = storage.raftOutline.CreateUnion(storage.wipeTower.Offset(extraDistanceAroundPart_�m));
            storage.raftOutline = storage.raftOutline.CreateUnion(supportGenerator.polygons.Offset(extraDistanceAroundPart_�m));
        }

        public static void GenerateRaftGCodeIfRequired(SliceDataStorage storage, ConfigSettings config, GCodeExport gcode)
        {
            if (config.raftBaseThickness_�m > 0 && config.raftInterfaceThicknes_�m > 0)
            {
                GCodePathConfig raftBaseConfig = new GCodePathConfig(config.firstLayerSpeed, config.raftBaseThickness_�m, "SUPPORT");
                GCodePathConfig raftMiddleConfig = new GCodePathConfig(config.raftPrintSpeed, config.raftInterfaceLinewidth_�m, "SUPPORT");
                GCodePathConfig raftInterfaceConfig = new GCodePathConfig(config.raftPrintSpeed, config.raftInterfaceLinewidth_�m, "SUPPORT");
                GCodePathConfig raftSurfaceConfig = new GCodePathConfig((config.raftSurfacePrintSpeed > 0) ? config.raftSurfacePrintSpeed : config.raftPrintSpeed, config.raftSurfaceLinewidth, "SUPPORT");

                {
                    gcode.writeComment("LAYER:-2");
                    gcode.writeComment("RAFT");
                    GCodePlanner gcodeLayer = new GCodePlanner(gcode, config.travelSpeed, config.minimumTravelToCauseRetraction_�m);
                    if (config.supportExtruder > 0)
                    {
                        gcodeLayer.setExtruder(config.supportExtruder);
                    }

                    gcodeLayer.setAlwaysRetract(true);
                    gcode.setZ(config.raftBaseThickness_�m);
                    gcode.setExtrusion(config.raftBaseThickness_�m, config.filamentDiameter_�m, config.extrusionMultiplier);
                    gcodeLayer.writePolygonsByOptimizer(storage.raftOutline, raftBaseConfig);

                    Polygons raftLines = new Polygons();
                    Infill.generateLineInfill(storage.raftOutline, raftLines, config.raftBaseThickness_�m, config.raftLineSpacing_�m, config.infillOverlapPerimeter_�m, 0);
                    gcodeLayer.writePolygonsByOptimizer(raftLines, raftBaseConfig);

                    gcodeLayer.writeGCode(false, config.raftBaseThickness_�m);
                }

                if (config.raftFanSpeedPercent > 0)
                {
                    gcode.writeFanCommand(config.raftFanSpeedPercent);
                }

                {
                    gcode.writeComment("LAYER:-1");
                    gcode.writeComment("RAFT");
                    GCodePlanner gcodeLayer = new GCodePlanner(gcode, config.travelSpeed, config.minimumTravelToCauseRetraction_�m);
                    gcodeLayer.setAlwaysRetract(true);
                    gcode.setZ(config.raftBaseThickness_�m + config.raftInterfaceThicknes_�m);
                    gcode.setExtrusion(config.raftInterfaceThicknes_�m, config.filamentDiameter_�m, config.extrusionMultiplier);

                    Polygons raftLines = new Polygons();
                    Infill.generateLineInfill(storage.raftOutline, raftLines, config.raftInterfaceLinewidth_�m, config.raftInterfaceLineSpacing, config.infillOverlapPerimeter_�m, 45);
                    gcodeLayer.writePolygonsByOptimizer(raftLines, raftInterfaceConfig);

                    gcodeLayer.writeGCode(false, config.raftInterfaceThicknes_�m);
                }

                for (int raftSurfaceLayer = 1; raftSurfaceLayer <= config.raftSurfaceLayers; raftSurfaceLayer++)
                {
                    gcode.writeComment("LAYER:FullRaft");
                    gcode.writeComment("RAFT");
                    GCodePlanner gcodeLayer = new GCodePlanner(gcode, config.travelSpeed, config.minimumTravelToCauseRetraction_�m);
                    gcodeLayer.setAlwaysRetract(true);
                    gcode.setZ(config.raftBaseThickness_�m + config.raftInterfaceThicknes_�m + config.raftSurfaceThickness * raftSurfaceLayer);
                    gcode.setExtrusion(config.raftSurfaceThickness, config.filamentDiameter_�m, config.extrusionMultiplier);

                    Polygons raftLines = new Polygons();
                    Infill.generateLineInfill(storage.raftOutline, raftLines, config.raftSurfaceLinewidth, config.raftSurfaceLineSpacing, config.infillOverlapPerimeter_�m, 90);
                    gcodeLayer.writePolygonsByOptimizer(raftLines, raftSurfaceConfig);

                    gcodeLayer.writeGCode(false, config.raftInterfaceThicknes_�m);
                }
            }
        }
    }
}