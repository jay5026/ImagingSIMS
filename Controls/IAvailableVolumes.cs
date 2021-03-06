﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImagingSIMS.Data.Rendering;

namespace ImagingSIMS.Controls
{
    public interface IAvailableVolumes
    {   
        /// <summary>
        /// Gets the selected volumes in the control that implements the interface.
        /// </summary>
        /// <returns></returns>
        List<Volume> GetSelectedVolumes();
        /// <summary>
        /// Gets all of the volumes available in the control that implements the interface.
        /// </summary>
        /// <returns></returns>
        List<Volume> GetAvailableVolumes();
        /// <summary>
        /// Removes the specified volumes from the control that implements the interface.
        /// </summary>
        /// <param name="volumesToRemove">Collection of volumes to remove</param>
        void RemoveVolumes(IEnumerable<Volume> volumesToRemove);
        /// <summary>
        /// Removes the specified volumes from the control that implements the interface.
        /// </summary>
        /// <param name="volumesToRemove">Array of volumes to remove</param>
        void RemoveVolumes(Volume[] volumesToRemove);
        /// <summary>
        /// Adds the specifed volumes to the control that implements the interface.
        /// </summary>
        /// <param name="volumesToAdd">Collection of volumes to add</param>
        void AddVolumes(IEnumerable<Volume> volumesToAdd);
        /// <summary>
        /// Adds the specifed volumes to the control that implements the interface.
        /// </summary>
        /// <param name="volumesToAdd">Array of volumes to add</param>
        void AddVolumes(Volume[] volumesToAdd);
        /// <summary>
        /// Replaces the specified volumes in the control that implements the interface.
        /// </summary>
        /// <param name="toReplace">Volume to remove</param>
        /// <param name="newvolume">Volume to add in place</param>
        void ReplaceVolume(Volume toReplace, Volume newvolume);
    }
}
