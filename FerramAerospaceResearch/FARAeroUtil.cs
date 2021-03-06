﻿/*
Ferram Aerospace Research v0.13.3
Copyright 2014, Michael Ferrara, aka Ferram4

    This file is part of Ferram Aerospace Research.

    Ferram Aerospace Research is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Kerbal Joint Reinforcement is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Ferram Aerospace Research.  If not, see <http://www.gnu.org/licenses/>.

    Serious thanks:		a.g., for tons of bugfixes and code-refactorings
            			Taverius, for correcting a ton of incorrect values
            			sarbian, for refactoring code for working with MechJeb, and the Module Manager 1.5 updates
            			ialdabaoth (who is awesome), who originally created Module Manager
            			Duxwing, for copy editing the readme
 * 
 * Kerbal Engineer Redux created by Cybutek, Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License
 *      Referenced for starting point for fixing the "editor click-through-GUI" bug
 *
 * Part.cfg changes powered by sarbian & ialdabaoth's ModuleManager plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/55219
 *
 * Toolbar integration powered by blizzy78's Toolbar plugin; used with permission
 *	http://forum.kerbalspaceprogram.com/threads/60863
 */


using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ferram4
{
    public static class FARAeroUtil
    {
        private static FloatCurve prandtlMeyerMach = null;
        private static FloatCurve prandtlMeyerAngle = null;
        public static double maxPrandtlMeyerTurnAngle = 0;
        private static FloatCurve pressureBehindShock = null;
        private static FloatCurve machBehindShock = null;
        private static FloatCurve stagnationPressure = null;
//        private static FloatCurve criticalMachNumber = null;
        //private static FloatCurve liftslope = null;
        private static FloatCurve maxPressureCoefficient = null;

        public static double areaFactor;
        public static double attachNodeRadiusFactor;
        public static double incompressibleRearAttachDrag;
        public static double sonicRearAdditionalAttachDrag;

        public static Dictionary<int, Vector3d> bodyAtmosphereConfiguration = null;
        public static int prevBody = -1;
        public static Vector3d currentBodyAtm = new Vector3d();
        public static double currentBodyTemp = 273.15;

        public static bool loaded = false;

        public static void SaveCustomAeroDataToConfig()
        {
            ConfigNode node = new ConfigNode("@FARAeroData[default]:FINAL");
            node.AddValue("%areaFactor", areaFactor);
            node.AddValue("%attachNodeDiameterFactor", attachNodeRadiusFactor * 2);
            node.AddValue("%incompressibleRearAttachDrag", incompressibleRearAttachDrag);
            node.AddValue("%sonicRearAdditionalAttachDrag", sonicRearAdditionalAttachDrag);
            node.AddValue("%ctrlSurfTimeConstant", FARControllableSurface.timeConstant);

            node.AddNode(new ConfigNode("!BodyAtmosphericData,*"));

            foreach (KeyValuePair<int, Vector3d> pair in bodyAtmosphereConfiguration)
            {
                node.AddNode(CreateAtmConfigurationConfigNode(pair.Key, pair.Value));
            }

            ConfigNode saveNode = new ConfigNode();
            saveNode.AddNode(node);
            saveNode.Save(KSPUtil.ApplicationRootPath.Replace("\\", "/") + "GameData/FerramAerospaceResearch/CustomFARAeroData.cfg");
        }

        private static ConfigNode CreateAtmConfigurationConfigNode(int bodyIndex, Vector3d atmProperties)
        {
            ConfigNode node = new ConfigNode("BodyAtmosphericData");
            node.AddValue("index", bodyIndex);

            double gasMolecularWeight = 8314.5 / atmProperties.z;
            node.AddValue("specHeatRatio", atmProperties.y);
            node.AddValue("gasMolecularWeight", gasMolecularWeight);

            return node;
        }

        public static void LoadAeroDataFromConfig()
        {
            if (loaded)
                return;

            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes("FARAeroData"))
            {
                if (node == null)
                    continue;

                if(node.HasValue("areaFactor"))
                    double.TryParse(node.GetValue("areaFactor"), out areaFactor);
                if (node.HasValue("attachNodeDiameterFactor"))
                {
                    double.TryParse(node.GetValue("attachNodeDiameterFactor"), out attachNodeRadiusFactor);
                    attachNodeRadiusFactor *= 0.5;
                }
                if (node.HasValue("incompressibleRearAttachDrag"))
                    double.TryParse(node.GetValue("incompressibleRearAttachDrag"), out incompressibleRearAttachDrag);
                if (node.HasValue("sonicRearAdditionalAttachDrag"))
                    double.TryParse(node.GetValue("sonicRearAdditionalAttachDrag"), out sonicRearAdditionalAttachDrag);

                if (node.HasValue("ctrlSurfTimeConstant"))
                    double.TryParse(node.GetValue("ctrlSurfTimeConstant"), out FARControllableSurface.timeConstant);

                FARAeroUtil.bodyAtmosphereConfiguration = new Dictionary<int, Vector3d>();
                foreach (ConfigNode bodyProperties in node.GetNodes("BodyAtmosphericData"))
                {
                    if (bodyProperties == null || !bodyProperties.HasValue("index") || !bodyProperties.HasValue("specHeatRatio") || !bodyProperties.HasValue("gasMolecularWeight"))
                        continue;

                    Vector3d Rgamma_and_gamma = new Vector3d();
                    double tmp;
                    double.TryParse(bodyProperties.GetValue("specHeatRatio"), out tmp);
                    Rgamma_and_gamma.y = tmp;

                    double.TryParse(bodyProperties.GetValue("gasMolecularWeight"), out tmp);

                    Rgamma_and_gamma.z = 8.3145 * 1000 / tmp;
                    Rgamma_and_gamma.x = Rgamma_and_gamma.y * Rgamma_and_gamma.z;

                    int index;
                    int.TryParse(bodyProperties.GetValue("index"), out index);

                    FARAeroUtil.bodyAtmosphereConfiguration.Add(index, Rgamma_and_gamma);
                }


            }

            //For any bodies that lack a configuration
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (bodyAtmosphereConfiguration.ContainsKey(body.flightGlobalsIndex))
                    continue;

                Vector3d Rgamma_and_gamma = new Vector3d();
                Rgamma_and_gamma.y = 1.4;
                Rgamma_and_gamma.z = 8.3145 * 1000 / 28.96;
                Rgamma_and_gamma.x = Rgamma_and_gamma.y * Rgamma_and_gamma.z;

                FARAeroUtil.bodyAtmosphereConfiguration.Add(body.flightGlobalsIndex, Rgamma_and_gamma);
            } 
            
            loaded = true;
        }

        public static double MaxPressureCoefficientCalc(double M)
        {
            if (M <= 0)
                return 0;
            double value;
            double gamma = currentBodyAtm.y;
            if (M <= 1)
                value = StagnationPressureCalc(M);
            else
            {

                value = (gamma + 1) * (gamma + 1);                  //Rayleigh Pitot Tube Formula; gives max stagnation pressure behind shock
                value *= M * M;
                value /= (4 * gamma * M * M - 2 * (gamma - 1));
                value = Math.Pow(value, 3.5);

                value *= (1 - gamma + 2 * gamma * M * M);
                value /= (gamma + 1);
            }
            value--;                                //and now to conver to pressure coefficient
            value *= 2 / (gamma * M * M);

            return value;
        }

        public static double StagnationPressureCalc(double M)
        {

            double ratio;
            double gamma = currentBodyAtm.y;
            ratio = M * M;
            ratio *= (gamma - 1);
            ratio *= 0.5;
            ratio++;

            ratio = Math.Pow(ratio, gamma / (gamma - 1));
            return ratio;
        }

        public static double PressureBehindShockCalc(double M)
        {
            double ratio;
            double gamma = currentBodyAtm.y;
            ratio = M * M;
            ratio--;
            ratio *= 2 * gamma;
            ratio /= (gamma + 1);
            ratio++;

            return ratio;

        }

        public static double MachBehindShockCalc(double M)
        {
            double ratio;
            double gamma = currentBodyAtm.y;
            ratio = (gamma - 1) * 0.5;
            ratio *= M * M;
            ratio++;
            ratio /= (gamma * M * M - (gamma - 1) * 0.5);
            ratio = Math.Sqrt(ratio);

            return ratio;
        }

        public static FloatCurve MaxPressureCoefficient
        {
            get
            {
                if (maxPressureCoefficient == null)
                {
                    MonoBehaviour.print("Stagnation Pressure Coefficient Curve Initialized");
                    maxPressureCoefficient = new FloatCurve();

                    double M = 0.05;
                    //float gamma = 1.4f;

                    maxPressureCoefficient.Add(0, 1);

                    if (currentBodyAtm == new Vector3d())
                    {
                        currentBodyAtm.y = 1.4;
                        currentBodyAtm.z = 8.3145 * 1000 / 28.96;
                        currentBodyAtm.x = currentBodyAtm.y * currentBodyAtm.z;
                    }

                    while (M < 50)
                    {
                        double value = 0;
                        if (M <= 1)
                        {
                            value = StagnationPressure.Evaluate((float)M);
                        }
                        else
                        {
                            value = (currentBodyAtm.y + 1) * (currentBodyAtm.y + 1);                  //Rayleigh Pitot Tube Formula; gives max stagnation pressure behind shock
                            value *= M * M;
                            value /= (4 * currentBodyAtm.y * M * M - 2 * (currentBodyAtm.y - 1));
                            value = Math.Pow(value, 3.5);

                            value *= (1 - currentBodyAtm.y + 2 * currentBodyAtm.y * M * M);
                            value /= (currentBodyAtm.y + 1);
                        }
                        value--;                                //and now to conver to pressure coefficient
                        value *= 2 / (currentBodyAtm.y * M * M);


                        maxPressureCoefficient.Add((float)M, (float)value);


                        if (M < 2)
                            M += 0.1;
                        else if (M < 5)
                            M += 0.5;
                        else
                            M += 2.5;
                    }



                }


                return maxPressureCoefficient;
            }
        }

        public static double LiftSlope(double input)
        {
            double tmp = input * input + 4;
            tmp = Math.Sqrt(tmp);
            tmp += 2;
            tmp = 1 / tmp;
            tmp *= 2 * Math.PI;

            return tmp;

        }

        public static FloatCurve PrandtlMeyerMach
        {
            get{
                if (prandtlMeyerMach == null)
                {
                    MonoBehaviour.print("Prandlt-Meyer Expansion Curves Initialized");
                    prandtlMeyerMach = new FloatCurve();
                    prandtlMeyerAngle = new FloatCurve();
                    double M = 1;
                    //float gamma = 1.4f;

                    double gamma_ = Math.Sqrt((currentBodyAtm.y + 1) / (currentBodyAtm.y - 1));

                    while (M < 250)
                    {
                        double mach = Math.Sqrt(M * M - 1);

                        double nu = Math.Atan(mach / gamma_);
                        nu *= gamma_;
                        nu -= Math.Atan(mach);
                        nu *= FARMathUtil.rad2deg;

                        double nu_mach = (currentBodyAtm.y - 1) / 2;
                        nu_mach *= M * M;
                        nu_mach++;
                        nu_mach *= M;
                        nu_mach = mach / nu_mach;
                        nu_mach *= FARMathUtil.rad2deg;

                        prandtlMeyerMach.Add((float)M, (float)nu, (float)nu_mach, (float)nu_mach);

                        nu_mach = 1 / nu_mach;

                        prandtlMeyerAngle.Add((float)nu, (float)M, (float)nu_mach, (float)nu_mach);

                        if (M < 3)
                            M += 0.1f;
                        else if (M < 10)
                            M += 0.5f;
                        else if (M < 25)
                            M += 2;
                        else
                            M += 25;
                    }

                    maxPrandtlMeyerTurnAngle = gamma_ - 1;
                    maxPrandtlMeyerTurnAngle *= 90;
                }
                return prandtlMeyerMach;
            }
        }

        public static FloatCurve PrandtlMeyerAngle
        {
            get
            {
                if (prandtlMeyerAngle == null)
                {
                    MonoBehaviour.print("Prandlt-Meyer Expansion Curves Initialized");
                    prandtlMeyerMach = new FloatCurve();
                    prandtlMeyerAngle = new FloatCurve();
                    double M = 1;
                    //float gamma = 1.4f;

                    double gamma_ = Math.Sqrt((currentBodyAtm.y + 1) / (currentBodyAtm.y - 1));

                    while (M < 250)
                    {
                        double mach = Math.Sqrt(M * M - 1);

                        double nu = Math.Atan(mach / gamma_);
                        nu *= gamma_;
                        nu -= Math.Atan(mach);
                        nu *= FARMathUtil.rad2deg;

                        double nu_mach = (currentBodyAtm.y - 1) / 2;
                        nu_mach *= M * M;
                        nu_mach++;
                        nu_mach *= M;
                        nu_mach = mach / nu_mach;
                        nu_mach *= FARMathUtil.rad2deg;

                        prandtlMeyerMach.Add((float)M, (float)nu, (float)nu_mach, (float)nu_mach);

                        nu_mach = 1 / nu_mach;

                        prandtlMeyerAngle.Add((float)nu, (float)M, (float)nu_mach, (float)nu_mach);

                        if (M < 3)
                            M += 0.1;
                        else if (M < 10)
                            M += 0.5;
                        else if (M < 25)
                            M += 2;
                        else
                            M += 25;
                    }

                    maxPrandtlMeyerTurnAngle = gamma_ - 1;
                    maxPrandtlMeyerTurnAngle *= 90;
                }
                return prandtlMeyerAngle;
            }
        }


        public static FloatCurve PressureBehindShock
        {
            get
            {
                if (pressureBehindShock == null)
                {
                    MonoBehaviour.print("Normal Shock Pressure Curve Initialized");
                    pressureBehindShock = new FloatCurve();
                    double ratio;
                    double d_ratio;
                    double M = 1;
                    //float gamma = 1.4f;
                    while (M < 250)  //Calculates the pressure behind a normal shock
                    {
                        ratio = M * M;
                        ratio--;
                        ratio *= 2 * currentBodyAtm.y;
                        ratio /= (currentBodyAtm.y + 1);
                        ratio++;

                        d_ratio = M * 4 * currentBodyAtm.y;
                        d_ratio /= (currentBodyAtm.y + 1);

                        pressureBehindShock.Add((float)M, (float)ratio, (float)d_ratio, (float)d_ratio);
                        if (M < 3)
                            M += 0.1;
                        else if (M < 10)
                            M += 0.5;
                        else if (M < 25)
                            M += 2;
                        else
                            M += 25;
                    }
                }
                return pressureBehindShock;
            }
        }

        public static FloatCurve MachBehindShock
        {
            get
            {
                if (machBehindShock == null)
                {
                    MonoBehaviour.print("Normal Shock Mach Number Curve Initialized");
                    machBehindShock = new FloatCurve();
                    double ratio;
                    double d_ratio;
                    double M = 1;
                    //float gamma = 1.4f;
                    while (M < 250)  //Calculates the pressure behind a normal shock
                    {
                        ratio = (currentBodyAtm.y - 1) / 2;
                        ratio *= M * M;
                        ratio++;
                        ratio /= (currentBodyAtm.y * M * M - (currentBodyAtm.y - 1) / 2);

                        d_ratio = 4 * currentBodyAtm.y * currentBodyAtm.y * Math.Pow(M, 4) - 4 * (currentBodyAtm.y - 1) * currentBodyAtm.y * M * M + Math.Pow(currentBodyAtm.y - 1, 2);
                        d_ratio = 1 / d_ratio;
                        d_ratio *= 4 * (currentBodyAtm.y * M * M - (currentBodyAtm.y - 1) / 2) * (currentBodyAtm.y - 1) * M - 8 * currentBodyAtm.y * M * (1 + (currentBodyAtm.y - 1) / 2 * M * M);

                        machBehindShock.Add((float)Math.Sqrt(M), (float)ratio);//, d_ratio, d_ratio);
                        if (M < 3)
                            M += 0.1;
                        else if (M < 10)
                            M += 0.5;
                        else if (M < 25)
                            M += 2;
                        else
                            M += 25;
                    }
                }
                return machBehindShock;
            }
        }

        public static FloatCurve StagnationPressure
        {
            get
            {
                if (stagnationPressure == null)
                {
                    MonoBehaviour.print("Stagnation Pressure Curve Initialized");
                    stagnationPressure = new FloatCurve();
                    double ratio;
                    double d_ratio;
                    double M = 0;
                    //float gamma = 1.4f;
                    while (M < 250)  //calculates stagnation pressure
                    {
                        ratio = M * M;
                        ratio *= (currentBodyAtm.y - 1);
                        ratio /= 2;
                        ratio++;
                        
                        d_ratio = ratio;

                        ratio = Math.Pow(ratio, currentBodyAtm.y / (currentBodyAtm.y - 1));

                        d_ratio = Math.Pow(d_ratio, (currentBodyAtm.y / (currentBodyAtm.y - 1)) - 1);
                        d_ratio *= M * currentBodyAtm.y;

                        stagnationPressure.Add((float)M, (float)ratio, (float)d_ratio, (float)d_ratio);
                        if (M < 3)
                            M += 0.1;
                        else if (M < 10)
                            M += 0.5;
                        else if (M < 25)
                            M += 2;
                        else
                            M += 25;
                    }
                }
                return stagnationPressure;
            }
        }


/*        public static FloatCurve CriticalMachNumber
        {
            get
            {
                if (criticalMachNumber == null)
                {
                    MonoBehaviour.print("Critical Mach Curve Initialized");
                    criticalMachNumber = new FloatCurve();
                    criticalMachNumber.Add(0, 0.98f);
                    criticalMachNumber.Add(Mathf.PI / 36, 0.86f);
                    criticalMachNumber.Add(Mathf.PI / 18, 0.65f);
                    criticalMachNumber.Add(Mathf.PI / 9, 0.35f);
                    criticalMachNumber.Add(Mathf.PI / 2, 0.3f);
                }
                return criticalMachNumber;
            }
        }*/

        private static float joolTempOffset = 0;

        public static float JoolTempOffset      //This is another kluge hotfix for the "Jool's atmosphere is below 0 Kelvin bug"
        {                                         //Essentially it just shifts its outer atmosphere temperature up to 4 Kelvin
            get{
                if(joolTempOffset == 0)
                {
                    CelestialBody Jool = null;
                    foreach (CelestialBody body in  FlightGlobals.Bodies)
                        if (body.GetName() == "Jool")
                        {
                            Jool = body;
                            break;
                        }
                    Jool.atmoshpereTemperatureMultiplier *= 0.5f;
                    float outerAtmTemp = FlightGlobals.getExternalTemperature(138000f, Jool) + 273.15f;
                    joolTempOffset = 25f - outerAtmTemp;
                    joolTempOffset = Mathf.Clamp(joolTempOffset, 0.1f, Mathf.Infinity);
                }
                

            return joolTempOffset;
            }
        }

        private static FloatCurve wingCamberFactor = null;
        private static FloatCurve wingCamberMoment = null;

        public static FloatCurve WingCamberFactor
        {
            get
            {
                if (wingCamberFactor == null)
                {
                    wingCamberFactor = new FloatCurve();
                    wingCamberFactor.Add(0, 0);
                    for (double i = 0.1; i <= 0.9; i += 0.1)
                    {
                        double tmp = i * 2;
                        tmp--;
                        tmp = Math.Acos(tmp);

                        tmp = tmp - Math.Sin(tmp);
                        tmp /= Math.PI;
                        tmp = 1 - tmp;

                        wingCamberFactor.Add((float)i, (float)tmp);
                    }
                    wingCamberFactor.Add(1, 1);
                }
                return wingCamberFactor;
            }
        }

        public static FloatCurve WingCamberMoment
        {
            get
            {
                if (wingCamberMoment == null)
                {
                    wingCamberMoment = new FloatCurve();
                    for (double i = 0; i <= 1; i += 0.1)
                    {
                        double tmp = i * 2;
                        tmp--;
                        tmp = Math.Acos(tmp);

                        tmp = (Math.Sin(2 * tmp) - 2 * Math.Sin(tmp)) / (8 * (Math.PI - tmp + Math.Sin(tmp)));

                        wingCamberMoment.Add((float)i, (float)tmp);
                    }
                }
                return wingCamberMoment;
            }
        }

        public static bool IsNonphysical(Part p)
        {
            return p.physicalSignificance == Part.PhysicalSignificance.NONE ||
                   (HighLogic.LoadedSceneIsEditor &&
                    p != EditorLogic.startPod &&
                    p.PhysicsSignificance == (int)Part.PhysicalSignificance.NONE);
        }

        // Parts currently added to the vehicle in the editor
        private static List<Part> CurEditorPartsCache = null;

        public static List<Part> CurEditorParts
        {
            get
            {
                if (CurEditorPartsCache == null)
                    CurEditorPartsCache = ListEditorParts(false);
                return CurEditorPartsCache;
            }
        }

        // Parts currently added, plus the ghost part(s) about to be attached
        private static List<Part> AllEditorPartsCache = null;

        public static List<Part> AllEditorParts
        {
            get
            {
                if (AllEditorPartsCache == null)
                    AllEditorPartsCache = ListEditorParts(true);
                return AllEditorPartsCache;
            }
        }

        public static void ResetEditorParts()
        {
            AllEditorPartsCache = CurEditorPartsCache = null;
        }

        // Checks if there are any ghost parts almost attached to the craft
        public static bool EditorAboutToAttach(bool move_too = false)
        {
            return HighLogic.LoadedSceneIsEditor &&
                   EditorLogic.SelectedPart != null &&
                   (EditorLogic.SelectedPart.potentialParent != null ||
                     (move_too && EditorLogic.SelectedPart == EditorLogic.startPod));
        }

        public static List<Part> ListEditorParts(bool include_selected)
        {
            var list = new List<Part>();

            if (EditorLogic.startPod)
                RecursePartList(list, EditorLogic.startPod);

            if (include_selected && EditorAboutToAttach())
            {
                RecursePartList(list, EditorLogic.SelectedPart);

                foreach (Part sym in EditorLogic.SelectedPart.symmetryCounterparts)
                    RecursePartList(list, sym);
            }

            return list;
        }

        private static void RecursePartList(List<Part> list, Part part)
        {
            list.Add(part);
            foreach (Part p in part.children)
                RecursePartList(list, p);
        }

        private static int RaycastMaskVal = 0, RaycastMaskEdit;
        private static String[] RaycastLayers = {
            "Default", "TransparentFX", "Local Scenery", "Disconnected Parts"
        };

        public static int RaycastMask
        {
            get
            {
                // Just to avoid the opaque integer constant; maybe it's enough to
                // document what layers come into it, but this is more explicit.
                if (RaycastMaskVal == 0)
                {
                    foreach (String name in RaycastLayers)
                        RaycastMaskVal |= (1 << LayerMask.NameToLayer(name));

                    // When parts are being dragged in the editor, they are put into this
                    // layer; however we have to raycast them, or the visible CoL will be
                    // different from the one after the parts are attached.
                    RaycastMaskEdit = RaycastMaskVal | (1 << LayerMask.NameToLayer("Ignore Raycast"));

                    Debug.Log("FAR Raycast mask: "+RaycastMaskVal+" "+RaycastMaskEdit);
                }

                return EditorAboutToAttach(true) ? RaycastMaskEdit : RaycastMaskVal;
            }
        }

        public static void AddBasicDragModule(Part p)
        {
            //if (IsNonphysical(p))
            //    return;

            //MonoBehaviour.print(p + ": " + p.PhysicsSignificance + " " + p.physicalSignificance);

            if (p.Modules.Contains("KerbalEVA"))
                return;

            p.angularDrag = 0;
            if (!p.Modules.Contains("ModuleResourceIntake"))
            {
                p.minimum_drag = 0;
                p.maximum_drag = 0;
                p.dragModelType = "override";
            }
            else
                return;

            p.AddModule("FARBasicDragModel");


            SetBasicDragModuleProperties(p);
        }

        public static void SetBasicDragModuleProperties(Part p)
        {
            FARBasicDragModel d = p.Modules["FARBasicDragModel"] as FARBasicDragModel;
            string title = p.partInfo.title.ToLowerInvariant();

            if (p.Modules.Contains("ModuleAsteroid"))
            {
                FARGeoUtil.BodyGeometryForDrag data = FARGeoUtil.CalcBodyGeometryFromMesh(p);

                FloatCurve TempCurve1 = new FloatCurve();
                double cd = 0.2; //cd based on diameter
                cd *= Math.Sqrt(data.crossSectionalArea / Math.PI) * 2 / data.area;

                TempCurve1.Add(-1, (float)cd);
                TempCurve1.Add(1, (float)cd);

                FloatCurve TempCurve2 = new FloatCurve();
                TempCurve2.Add(-1, 0);
                TempCurve2.Add(1, 0);

                FloatCurve TempCurve4 = new FloatCurve();
                TempCurve2.Add(-1, 0);
                TempCurve2.Add(1, 0);

                FloatCurve TempCurve3 = new FloatCurve();
                TempCurve3.Add(-1, 0);
                TempCurve3.Add(1, 0);



                d.BuildNewDragModel(data.area * FARAeroUtil.areaFactor, TempCurve1, TempCurve2, TempCurve4, TempCurve3, data.originToCentroid, data.majorMinorAxisRatio, 0, data.taperCrossSectionArea, double.MaxValue, double.MaxValue);
                return;
            }
            else if (FARPartClassification.IncludePartInGreeble(p, title))
            {
                FloatCurve TempCurve1 = new FloatCurve();
                /*if (title.Contains("heatshield") || (title.Contains("heat") && title.Contains("shield")))
                    TempCurve1.Add(-1, 0.3f);
                else*/
                TempCurve1.Add(-1, 0);
                TempCurve1.Add(0, 0.02f);
                TempCurve1.Add(1, 0);

                FloatCurve TempCurve2 = new FloatCurve();
                TempCurve2.Add(-1, 0);
                TempCurve2.Add(1, 0);

                FloatCurve TempCurve4 = new FloatCurve();
                TempCurve2.Add(-1, 0);
                TempCurve2.Add(1, 0);

                FloatCurve TempCurve3 = new FloatCurve();
                TempCurve3.Add(-1, 0);
                TempCurve3.Add(1, 0);


                double area = FARGeoUtil.CalcBodyGeometryFromMesh(p).area;

                d.BuildNewDragModel(area * FARAeroUtil.areaFactor, TempCurve1, TempCurve2, TempCurve4, TempCurve3, Vector3.zero, 1, 0, 0, double.MaxValue, double.MaxValue);
                return;
            }
            else
            {
                FARGeoUtil.BodyGeometryForDrag data = FARGeoUtil.CalcBodyGeometryFromMesh(p);
                FloatCurve TempCurve1 = new FloatCurve();
                FloatCurve TempCurve2 = new FloatCurve();
                FloatCurve TempCurve4 = new FloatCurve();
                FloatCurve TempCurve3 = new FloatCurve();
                double YmaxForce = double.MaxValue;
                double XZmaxForce = double.MaxValue;

                double Cn1, Cn2, cutoffAngle, cosCutoffAngle = 0;

                if (title.Contains("truss") || title.Contains("strut") || title.Contains("railing") || p.Modules.Contains("ModuleWheel"))
                {
                    TempCurve1.Add(-1, 0.1f);
                    TempCurve1.Add(1, 0.1f);
                    TempCurve2.Add(-1, 0f);
                    TempCurve2.Add(1, 0f);
                    TempCurve3.Add(-1, 0f);
                    TempCurve3.Add(1, 0f);
                }
                else if (title.Contains("plate") || title.Contains("panel"))
                {
                    TempCurve1.Add(-1, 1.2f);
                    TempCurve1.Add(0, 0f);
                    TempCurve1.Add(1, 1.2f);
                    TempCurve2.Add(-1, 0f);
                    TempCurve2.Add(1, 0f);
                    TempCurve3.Add(-1, 0f);
                    TempCurve3.Add(1, 0f);
                }
                else
                {

                    if (data.taperRatio <= 1)
                    {
                        Cn1 = NormalForceCoefficientTerm1(data.finenessRatio, data.taperRatio, data.crossSectionalArea, data.area);
                        Cn2 = NormalForceCoefficientTerm2(data.finenessRatio, data.taperRatio, data.crossSectionalArea, data.area);
                        cutoffAngle = cutoffAngleForLift(data.finenessRatio, data.taperRatio, data.crossSectionalArea, data.area);

                        cosCutoffAngle = -Math.Cos(cutoffAngle);

                        double axialPressureDrag = PressureDragDueToTaperingConic(data.finenessRatio, data.taperRatio, data.crossSectionalArea, data.area);

                        TempCurve1.Add(-1, (float)axialPressureDrag);
                        TempCurve1.Add(0, (float)Cn2);
                        TempCurve1.Add(1, (float)axialPressureDrag);


                        if (cutoffAngle > 30)
                            TempCurve2.Add((float)cosCutoffAngle, 0, (float)Cn1, 0);
                        else
                            TempCurve2.Add(-0.9f, 0, (float)Cn1, 0);
                        TempCurve2.Add(-0.8660f, (float)(Math.Cos((Math.PI * 0.5 - Math.Acos(0.8660f)) * 0.5) * Math.Sin(2 * (Math.PI * 0.5 - Math.Acos(0.8660f))) * Cn1), 0, 0);
                        TempCurve2.Add(0, 0);
                        TempCurve2.Add(0.8660f, (float)(Math.Cos((Math.PI * 0.5 - Math.Acos(0.8660f)) * 0.5) * Math.Sin(2 * (Math.PI * 0.5 - Math.Acos(0.8660f))) * Cn1), 0, 0);
                        TempCurve2.Add(1, 0, (float)Cn1, (float)Cn1);

                        TempCurve4.Add(-1, 0, 0, 0);
                        TempCurve4.Add(-0.95f, (float)(Math.Pow(Math.Sin(Math.Acos(0.95f)), 2) * Cn2 * -0.95f));
                        TempCurve4.Add(-0.8660f, (float)(Math.Pow(Math.Sin(Math.Acos(0.8660f)), 2) * Cn2 * -0.8660f));
                        TempCurve4.Add(-0.5f, (float)(Math.Pow(Math.Sin(Math.Acos(0.5f)), 2) * Cn2 * -0.5f));
                        TempCurve4.Add(0, 0);
                        TempCurve4.Add(0.5f, (float)(Math.Pow(Math.Sin(Math.Acos(0.5f)), 2) * Cn2 * 0.5f));
                        TempCurve4.Add(0.8660f, (float)(Math.Pow(Math.Sin(Math.Acos(0.8660f)), 2) * Cn2 * 0.8660f));
                        TempCurve4.Add(0.95f, (float)(Math.Pow(Math.Sin(Math.Acos(0.95f)), 2) * Cn2 * 0.95f));
                        TempCurve4.Add(1, 0, 0, 0);
                    }
                    else
                    {
                        Cn1 = NormalForceCoefficientTerm1(data.finenessRatio, 1 / data.taperRatio, data.crossSectionalArea, data.area);
                        Cn2 = NormalForceCoefficientTerm2(data.finenessRatio, 1 / data.taperRatio, data.crossSectionalArea, data.area);
                        cutoffAngle = cutoffAngleForLift(data.finenessRatio, 1 / data.taperRatio, data.crossSectionalArea, data.area);

                        cosCutoffAngle = Math.Cos(cutoffAngle);

                        double axialPressureDrag = PressureDragDueToTaperingConic(data.finenessRatio, 1 / data.taperRatio, data.crossSectionalArea, data.area);

                        TempCurve1.Add(-1, (float)axialPressureDrag, 0, 0);
                        TempCurve1.Add(0, (float)Cn2, 0, 0);
                        TempCurve1.Add(1, (float)axialPressureDrag, 0, 0);


                        TempCurve2.Add(-1, 0, (float)-Cn1, (float)-Cn1);
                        TempCurve2.Add(-0.8660f, (float)((-Math.Cos((Math.PI *0.5 - Math.Acos(0.8660)) *0.5) * Math.Sin(2 * (Math.PI *0.5 - Math.Acos(0.8660))) * Cn1)), 0, 0);
                        TempCurve2.Add(0, 0);
                        TempCurve2.Add(0.8660f, (float)((-Math.Cos((Math.PI *0.5 - Math.Acos(0.8660)) *0.5) * Math.Sin(2 * (Math.PI *0.5 - Math.Acos(0.8660))) * Cn1)), 0, 0);
                        if (cutoffAngle > 30)
                            TempCurve2.Add((float)cosCutoffAngle, 0, (float)-Cn1, 0);
                        else
                            TempCurve2.Add(0.9f, 0, (float)-Cn1, 0);

                        TempCurve4.Add(-1, 0, 0, 0);
                        TempCurve4.Add(-0.95f, (float)(Math.Pow(Math.Sin(Math.Acos(0.95)), 2) * Cn2 * -0.95));
                        TempCurve4.Add(-0.8660f, (float)(Math.Pow(Math.Sin(Math.Acos(0.8660)), 2) * Cn2 * -0.8660));
                        TempCurve4.Add(-0.5f, (float)(Math.Pow(Math.Sin(Math.Acos(0.5)), 2) * Cn2 * -0.5));
                        TempCurve4.Add(0, 0);
                        TempCurve4.Add(0.5f, (float)(Math.Pow(Math.Sin(Math.Acos(0.5)), 2) * Cn2 * 0.5));
                        TempCurve4.Add(0.8660f, (float)(Math.Pow(Math.Sin(Math.Acos(0.8660)), 2) * Cn2 * 0.8660));
                        TempCurve4.Add(0.95f, (float)(Math.Pow(Math.Sin(Math.Acos(0.95)), 2) * Cn2 * 0.95));
                        TempCurve4.Add(1, 0, 0, 0);
                    }


                    /*                    TempCurve2.Add(-1, 0);
                                        TempCurve2.Add(-0.866f, -0.138f);
                                        TempCurve2.Add(-0.5f, -0.239f, 0, 0);
                                        TempCurve2.Add(0, 0);
                                        TempCurve2.Add(0.5f, 0.239f, 0, 0);
                                        TempCurve2.Add(0.866f, 0.138f);
                                        TempCurve2.Add(1, 0);*/

                    /*                    if (p.partInfo.title.ToLowerInvariant().Contains("nose"))
                                        {
                                            TempCurve3.Add(-1, -0.1f, 0, 0);
                                            TempCurve3.Add(-0.5f, -0.1f, 0, 0);
                                            TempCurve3.Add(0, -0.1f, 0, 0);
                                            TempCurve3.Add(0.8660f, 0f, 0, 0);
                                            TempCurve3.Add(1, 0.1f, 0, 0);
                                        }
                                        else
                                        {*/

                    float cdM = (float)MomentDueToTapering(data.finenessRatio, data.taperRatio, data.crossSectionalArea, data.area);

                    TempCurve3.Add(-1, cdM);
                    TempCurve3.Add(-0.5f, cdM * 2);
                    TempCurve3.Add(0, cdM * 3);
                    TempCurve3.Add(0.5f, cdM * 2);
                    TempCurve3.Add(1, cdM);


                    if (HighLogic.LoadedSceneIsFlight && !FARAeroStress.PartIsGreeble(p, data.crossSectionalArea, data.finenessRatio, data.area) && FARDebugValues.allowStructuralFailures)
                    {
                        FARPartStressTemplate template = FARAeroStress.DetermineStressTemplate(p);

                        YmaxForce = template.YmaxStress;    //in MPa
                        YmaxForce *= data.crossSectionalArea;

                        /*XZmaxForce = 2 * Math.Sqrt(data.crossSectionalArea / Math.PI);
                        XZmaxForce = XZmaxForce * data.finenessRatio * XZmaxForce;
                        XZmaxForce *= template.XZmaxStress;*/

                        XZmaxForce = template.XZmaxStress * data.area * 0.5;

                        Debug.Log("Template: " + template.name + " YmaxForce: " + YmaxForce + " XZmaxForce: " + XZmaxForce);
                    }

                }
//                if (p.Modules.Contains("FARPayloadFairingModule"))
//                    data.area /= p.symmetryCounterparts.Count + 1;

                d.BuildNewDragModel(data.area * FARAeroUtil.areaFactor, TempCurve1, TempCurve2, TempCurve4, TempCurve3, data.originToCentroid, data.majorMinorAxisRatio, cosCutoffAngle, data.taperCrossSectionArea, YmaxForce, XZmaxForce);
                return;
            }
        }


        //Approximate drag of a tapering conic body
        public static double PressureDragDueToTaperingConic(double finenessRatio, double taperRatio, double crossSectionalArea, double surfaceArea)
        {
            double b = 1 + (taperRatio / (1 - taperRatio));

            double refbeta = 2f * FARMathUtil.Clamp(finenessRatio, 1, double.PositiveInfinity) / Math.Sqrt(Math.Pow(2.5f, 2) - 1);     //Reference beta for the calculation; currently based at Mach 2.5
            double cdA;
            if (double.IsNaN(b) || double.IsInfinity(b))
            {
                return 0;
            }
            else if (b > 1)
            {
                cdA = 2 * (2 * b * b - 2 * b + 1) * Math.Log(2 * refbeta) - 2 * Math.Pow(b - 1, 2) * Math.Log(1 - 1 / b) - 1;
                cdA /= b * b * b * b;
                //Based on linear supersonic potential for a conic body, from
                //The Theoretical Wave-Drag of Some Bodies of Revolution, L. E. FRAENKEL, 1955
                //MINISTRY OF SUPPLY
                //AERONAUTICAL RESEARCH COUNCIL
                //REPORTS AND MEMORANDA
                //London
            }
            else
            {
                cdA = 2 * Math.Log(2 * refbeta) - 1;
            }

            cdA *= 0.25 / (finenessRatio * finenessRatio);
            cdA *= crossSectionalArea / surfaceArea;

            cdA /= 1.31;   //Approximate subsonic drag...

            //if (float.IsNaN(cdA))
            //    return 0;

            return cdA;
        }
        
        //Approximate drag of a tapering parabolic body
        public static double PressureDragDueToTaperingParabolic(double finenessRatio, double taperRatio, double crossSectionalArea, double surfaceArea)
        {
            double exponent = 2 + (Math.Sqrt(FARMathUtil.Clamp(finenessRatio, 0.1, double.PositiveInfinity)) - 2.2) / 3;

            double cdA = 4.6 * Math.Pow(Math.Abs(taperRatio - 1), 2 * exponent);
            cdA *= 0.25 / (finenessRatio * finenessRatio);
            cdA *= crossSectionalArea / surfaceArea;

            cdA /= 1.35;   //Approximate subsonic drag...


            double taperCrossSectionArea = Math.Sqrt(crossSectionalArea / Math.PI);
            taperCrossSectionArea *= taperRatio;
            taperCrossSectionArea = Math.Pow(taperCrossSectionArea, 2) * Math.PI;

            taperCrossSectionArea = Math.Abs(taperCrossSectionArea - crossSectionalArea);      //This is the cross-sectional area of the tapered section

            double maxcdA = taperCrossSectionArea * (incompressibleRearAttachDrag + sonicRearAdditionalAttachDrag);
            maxcdA /= surfaceArea;      //This is the maximum drag that this part can create

            cdA = FARMathUtil.Clamp(cdA, 0, maxcdA);      //make sure that stupid amounts of drag don't exist

            return cdA;
        }


        //This returns the normal force coefficient based on surface area due to potential flow
        public static double NormalForceCoefficientTerm1(double finenessRatio, double taperRatio, double crossSectionalArea, double surfaceArea)
        {
            double Cn1 = 0;
            //float radius = Mathf.Sqrt(crossSectionalArea * 0.318309886184f);
            //float length = radius * 2 * finenessRatio;

            /*//Assuming a linearly tapered cone
            Cn1 = 1 + (taperRatio - 1) + (taperRatio - 1) * (taperRatio - 1) / 3;
            Cn1 *= Mathf.PI * radius * radius * length;*/

            Cn1 = crossSectionalArea * (1 - taperRatio * taperRatio);
            Cn1 /= surfaceArea;

            return Cn1;
            //return 0;
        }

        //This returns the normal force coefficient based on surface area due to viscous flow
        public static double NormalForceCoefficientTerm2(double finenessRatio, double taperRatio, double crossSectionalArea, double surfaceArea)
        {
            double Cn2 = 0;
            double radius = Math.Sqrt(crossSectionalArea * 0.318309886184f);
            double length = radius * 2 * finenessRatio;

            //Assuming a linearly tapered cone
            Cn2 = radius * (1 + taperRatio) * length * 0.5f;
            Cn2 *= 2 * 1.2f;
            Cn2 /= surfaceArea;

            return Cn2;
            //return 0;
        }

        public static double cutoffAngleForLift(double finenessRatio, double taperRatio, double crossSectionalArea, double surfaceArea)
        {
            double angle = 0;

            angle = (1 - taperRatio) / (2 * finenessRatio);
            angle = Math.Atan(angle);

            return angle;
        }

        public static double MomentDueToTapering(double finenessRatio, double taperRatio, double crossSectionalArea, double surfaceArea)
        {

            double rad = crossSectionalArea / Math.PI;
            rad = Math.Sqrt(rad);

            double cdM = 0f;
            if (taperRatio < 1)
            {
                double dragDueToTapering = PressureDragDueToTaperingConic(finenessRatio, taperRatio, crossSectionalArea, surfaceArea);

                cdM -= dragDueToTapering * rad * taperRatio;    //This applies the drag force over the front of the tapered area multiplied by the distance it acts from the center of the part (radius * taperRatio)
            }
            else
            {
                taperRatio = 1 / taperRatio;
                double dragDueToTapering = PressureDragDueToTaperingConic(finenessRatio, taperRatio, crossSectionalArea, surfaceArea);

                cdM += dragDueToTapering * rad * taperRatio;    //This applies the drag force over the front of the tapered area multiplied by the distance it acts from the center of the part (radius * taperRatio)
            }

            //cdM *= 1 / surfaceArea;

            return cdM;
        }

        //This approximates e^x; it's slightly inaccurate, but good enough.  It's much faster than an actual exponential function
        //It runs on the assumption e^x ~= (1 + x/256)^256
        public static double ExponentialApproximation(double x)
        {
            double exp = 1d + x * 0.00390625;
            exp *= exp;
            exp *= exp;
            exp *= exp;
            exp *= exp;
            exp *= exp;
            exp *= exp;
            exp *= exp;
            exp *= exp;

            return exp;
        }


        public static double GetMachNumber(CelestialBody body, double altitude, Vector3 velocity)
        {
            double MachNumber = 0;
            if (HighLogic.LoadedSceneIsFlight)
            {
                //continue updating Mach Number for debris
                UpdateCurrentActiveBody(body);
                double temp = Math.Max(0.1, currentBodyTemp + FlightGlobals.getExternalTemperature((float)altitude, body));
                double Soundspeed = Math.Sqrt(temp * currentBodyAtm.x);// * 401.8f;              //Calculation for speed of sound in ideal gas using air constants of gamma = 1.4 and R = 287 kJ/kg*K

                MachNumber = velocity.magnitude / Soundspeed;

                if (MachNumber < 0)
                    MachNumber = 0;

            }
            return MachNumber;
        }

        public static double GetCurrentDensity(CelestialBody body, Vector3 worldLocation)
        {
            UpdateCurrentActiveBody(body);

            double temp = Math.Max(0.1, currentBodyTemp + FlightGlobals.getExternalTemperature(worldLocation));

            double pressure = FlightGlobals.getStaticPressure(worldLocation, body) * 101300;     //Need to convert atm to Pa

            return pressure / (temp * currentBodyAtm.z);
        }

        public static double GetCurrentDensity(CelestialBody body, double altitude)
        {
            UpdateCurrentActiveBody(body);

            if (altitude > body.maxAtmosphereAltitude)
                return 0;

            double temp = Math.Max(0.1, currentBodyTemp + FlightGlobals.getExternalTemperature((float)altitude, body));

            double pressure = FlightGlobals.getStaticPressure(altitude, body) * 101300;     //Need to convert atm to Pa

            return pressure / (temp * currentBodyAtm.z);
        }

        // Vessel has altitude and cached pressure, and both density and sound speed need temperature
        public static double GetCurrentDensity(Vessel vessel, out double soundspeed)
        {
            double altitude = vessel.altitude;
            CelestialBody body = vessel.mainBody;

            soundspeed = 1e+6f;

            if ((object)body == null || altitude > body.maxAtmosphereAltitude)
                return 0;

            UpdateCurrentActiveBody(body);

            double temp = Math.Max(0.1, currentBodyTemp + FlightGlobals.getExternalTemperature((float)altitude, body));
            double pressure = (float)vessel.staticPressure * 101300f;     //Need to convert atm to Pa

            soundspeed = Math.Sqrt(temp * currentBodyAtm.x); // * 401.8f;              //Calculation for speed of sound in ideal gas using air constants of gamma = 1.4 and R = 287 kJ/kg*K

            return pressure / (temp * currentBodyAtm.z);
        }

        public static void UpdateCurrentActiveBody(CelestialBody body)
        {
            if ((object)body != null && body.flightGlobalsIndex != prevBody)
            {
                UpdateCurrentActiveBody(body.flightGlobalsIndex);
//                if (body.name == "Jool" || body.name == "Sentar")
                if(body.pqsController == null)
                    currentBodyTemp += FARAeroUtil.JoolTempOffset;
            }
        }

        public static void UpdateCurrentActiveBody(int index)
        {
            if (index != prevBody)
            {
                prevBody = index;
                currentBodyAtm = bodyAtmosphereConfiguration[prevBody];
                currentBodyTemp = 273.15f;
                prandtlMeyerMach = null;
                prandtlMeyerAngle = null;
                pressureBehindShock = null;
                machBehindShock = null;
                stagnationPressure = null;
                maxPressureCoefficient = null;
            }
        }
        
        //Based on NASA Contractor Report 187173, Exact and Approximate Oblique Shock Equations for Real-Time Applications
        public static double CalculateSinWeakObliqueShockAngle(double MachNumber, double gamma, double deflectionAngle)
        {
            double M2 = MachNumber * MachNumber;
            double recipM2 = 1 / M2;
            double sin2def = Math.Sin(deflectionAngle);
            sin2def *= sin2def;

            double b = M2 + 2;
            b *= recipM2;
            b += gamma * sin2def;
            b = -b;

            double c = gamma + 1;
            c *= c * 0.25f;
            c += (gamma - 1) * recipM2;
            c *= sin2def;
            c += (2 * M2 + 1) * recipM2 * recipM2;

            double d = sin2def - 1;
            d *= recipM2 * recipM2;

            double Q = c * 0.33333333 - b * b * 0.111111111;
            double R = 0.16666667 * b * c - 0.5f * d - 0.037037037 * b * b * b;
            double D = Q * Q * Q + R * R;

            if (D > 0.001)
                return double.NaN;

            double phi = Math.Atan(Math.Sqrt(FARMathUtil.Clamp(-D, 0, double.PositiveInfinity)) / R);
            if (R < 0)
                phi += Math.PI;
            phi *= 0.33333333;

            double chiW = -0.33333333 * b - Math.Sqrt(FARMathUtil.Clamp(-Q, 0, double.PositiveInfinity)) * (Math.Cos(phi) - 1.7320508f * Math.Sin(phi));

            double betaW = Math.Sqrt(FARMathUtil.Clamp(chiW, 0, double.PositiveInfinity));

            return betaW;
        }

        public static double CalculateSinMaxShockAngle(double MachNumber, double gamma)
        {
            double M2 = MachNumber * MachNumber;
            double gamP1_2_M2 = (gamma + 1) * 0.5 * M2;

            double b = gamP1_2_M2;
            b = 2 - b;
            b *= M2;

            double a = gamma * M2 * M2;

            double c = gamP1_2_M2 + 1;
            c = -c;

            double tmp = b * b - 4 * a * c;

            double sin2def = -b + Math.Sqrt(FARMathUtil.Clamp(tmp, 0, double.PositiveInfinity));
            sin2def /= (2 * a);

            return Math.Sqrt(sin2def);
        }

        public static double MaxShockAngleCheck(double MachNumber, double gamma, out bool attachedShock)
        {
            double M2 = MachNumber * MachNumber;
            double gamP1_2_M2 = (gamma + 1) * 0.5 * M2;

            double b = gamP1_2_M2;
            b = 2 - b;
            b *= M2;

            double a = gamma * M2 * M2;

            double c = gamP1_2_M2 + 1;
            c = -c;

            double tmp = b * b - 4 * a * c;

            if (tmp > 0)
                attachedShock = true;
            else
                attachedShock = false;

            return tmp;
        }

        /*public static double SupersonicWingCna(double AR, double tanSweep, double B, double taperRatio, out bool subsonicLE)
        {
            //double B = Math.Sqrt(M * M - 1);
            double m = 1 / tanSweep;

            double AR_ = AR * B;
            double m_ = m * B;
            double k = taperRatio + 1;
            k = AR_ * k;
            k = k / (k - 4 * m_ * (1 - taperRatio));

            double machLineVal = 4 * m_ * taperRatio;
            machLineVal /= (taperRatio + 1) * (1 - m_);

            if (m_ >= 1)    //Supersonic leading edge
            {
                subsonicLE = false;
                if (AR_ < machLineVal)      //Mach line intercepts tip chord
                {
                    double m_k = m_ * k;
                    double fourm_k = 4 * m_k;
                    double invkm_kplusone = (m_k + 1) * k;
                    invkm_kplusone = 1 / invkm_kplusone;

                    double line4 = fourm_k + AR_ * (3 * k + 1);
                    line4 = (fourm_k * (AR_ - 1) + AR_ * (k - 1)) / line4;
                    line4 = Math.Acos(line4);

                    double tmp = (m_ - 1) * invkm_kplusone;
                    tmp = Math.Sqrt(tmp);
                    line4 *= tmp;

                    tmp = fourm_k + AR_ * (1 + 3 * k);
                    tmp *= tmp;
                    tmp /= (4 * AR_ * (k + 1));
                    line4 *= tmp;

                    double line3 = fourm_k - AR_ * (k - 1);
                    line3 = (fourm_k * (1 - AR_) + AR_ * (3 * k + 1)) / line4;
                    line3 = Math.Acos(line3);

                    tmp = (m_ + 1) * invkm_kplusone;
                    tmp = Math.Sqrt(tmp);
                    line3 *= tmp;

                    tmp = fourm_k - AR_ * (k - 1);
                    tmp *= tmp;
                    tmp /= (4 * AR_ * (k - 1));
                    line3 *= -tmp;


                    double line2 = fourm_k + AR_ * (k - 1);
                    line2 = (fourm_k * (AR_ - 1) - AR_ * (k + 3)) / line4;
                    line2 = -Math.Acos(line2);

                    line2 += Math.Acos(-1 / m_k);

                    tmp = (m_k + 1) * (m_k - 1);
                    tmp = Math.Sqrt(tmp);
                    tmp = k * Math.Sqrt(m_ * m_ - 1) / tmp;
                    line2 *= tmp;

                    tmp = Math.Acos(1 / m_);
                    tmp /= k;
                    line2 += tmp;

                    tmp = fourm_k + AR_ * (k - 1);
                    tmp *= tmp;
                    tmp /= (2 * AR_ * (k * k - 1));
                    line2 *= tmp;

                    double Cna = line2 + line3 + line4;
                    Cna /= (Math.PI * B * Math.Sqrt(m_ * m_ - 1));
                    return Cna;
                }
                else            //Mach line intercepts trailing edge
                {
                    double m_k = m_ * k;
                    double fourm_k = 4 * m_k;
                    double line2 = fourm_k - AR_ * (k - 1);
                    line2 *= Math.PI * line2;
                    line2 /= 4 * AR_ * (k - 1);

                    double tmp = (m_k + 1) * k;
                    tmp = m_ + 1 / tmp;
                    tmp = Math.Sqrt(tmp);

                    line2 *= -tmp;

                    double line1 = (m_k - 1) * (m_k + 1);
                    line1 = Math.Sqrt(line1);

                    tmp = Math.Sqrt(m_ * m_ - 1) * k;
                    line1 = tmp / line1;

                    line1 *= Math.Acos(-1 / m_k);

                    line1 += Math.Acos(1 / m_) / k;

                    tmp = fourm_k + AR_ * (k - 1);
                    tmp *= tmp;
                    tmp /= (2 * AR_ * (k * k - 1));

                    line1 *= tmp;

                    double Cna = line1 + line2;
                    Cna /= (Math.PI * B * Math.Sqrt(m_ * m_ - 1));
                    return Cna;
                }
            }
            else                       //Subsonic leading edge
            {
                subsonicLE = true;
                double w = 4 * m_ / (AR_ * (1 + taperRatio));
                double n = FARMathUtil.Clamp(1 - (1 - taperRatio) * w, 0, 1);

                //Debug.Log("n " + n);

                double longSqrtTerm = (1 + m_) * (n + 1) + w * (m_ - 1);
                longSqrtTerm *= (w + n - 1);
                longSqrtTerm = Math.Sqrt(longSqrtTerm);

                double smallACosTerm = 1 + m_ * n + w * (m_ - 1);
                smallACosTerm /= (m_ + n);
                smallACosTerm = Math.Acos(smallACosTerm);

                double invOneMinusMPow = Math.Pow(1 - m_, -1.5);

                double line4 = 2 * (m_ + n) * (1 - m_) + (1 + n);
                line4 = (1 + m_) * (1 + n) - w * (1 - m_) / line4;
                line4 *= longSqrtTerm;

                double line3 = (n + w) * (m_ - n) + 2 * (1 - w) + m_ + n;
                line3 /= (1 + n + w) * (m_ + n);
                line3 = Math.Acos(line3);

                double tmp = 1 + n + w;
                tmp *= tmp;
                tmp *= 0.25 * Math.Pow(1 + n, -1.5);
                line3 *= tmp;

                line3 -= smallACosTerm * invOneMinusMPow;

                double line34 = line3 + line4;

                tmp = 4 * AR;
                tmp /= (Math.PI * Math.Sqrt(1 + m_));
                line34 *= tmp;

                double line2 = w * n * (m_ - 1) + m_ * (n * n - 1) * Math.Sqrt(1 + m_);
                line2 /= (m_ + n) * (n * n - 1) * (m_ - 1);
                line2 *= longSqrtTerm;
                
                double line1 = (1 + m_) * (n * n - 1) + w * (1 + m_ * n);
                line1 /= w * (m_ + n);
                line1 = -Math.Asin(line1);
                line1 += Math.Asin(n);

                line1 *= w * w * Math.Pow(Math.Abs(1 - n * n), -1.5);

                tmp = n * w * w / (1 - n * n);
                line1 += tmp;

                //Debug.Log("line1 " + line1);

                tmp = Math.Sqrt(1 + m) * invOneMinusMPow * smallACosTerm;
                line1 += tmp;

                //Debug.Log("line1 " + line1);

                double line12 = line1 + line2;
                line12 *= AR;
                line12 /= FARMathUtil.CompleteEllipticIntegralSecondKind(m_, 1e-6);


                double Cna = line12 + line34;
                //Debug.Log("Cna " + Cna);
                return Cna;
            }
        }


        public static double SubsonicLECnaa(double E, double B, double tanSweep, double AoA)
        {
            return 0;
            double Eparam = E * B / tanSweep;
            double AoAparam = Math.Tan(AoA) * B;
            if (AoA > 1)
                AoAparam = 1 / AoAparam;

            double Cnaa = 0;

            return Cnaa;
        }*/
    }
}
