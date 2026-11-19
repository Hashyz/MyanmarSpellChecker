// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Extensibility
{
    [CompilerGenerated]
    [Guid("289E9AF1-4973-11D1-AE81-00A0C90F26F4")]
    [TypeIdentifier("ac0714f2-3d04-11d1-ae7d-00a0c90f26f4", "Extensibility.ext_ConnectMode")]
    public enum ext_ConnectMode
    {
        ext_cm_AfterStartup = 0,
        ext_cm_Startup = 1,
        ext_cm_External = 2,
        ext_cm_CommandLine = 3,
        ext_cm_Solution = 4,
        ext_cm_UISetup = 5
    }

    [CompilerGenerated]
    [Guid("289E9AF2-4973-11D1-AE81-00A0C90F26F4")]
    [TypeIdentifier("ac0714f2-3d04-11d1-ae7d-00a0c90f26f4", "Extensibility.ext_DisconnectMode")]
    public enum ext_DisconnectMode
    {
        ext_dm_HostShutdown = 0,
        ext_dm_UserClosed = 1,
        ext_dm_UISetupComplete = 2,
        ext_dm_SolutionClosed = 3
    }

    [ComImport]
    [CompilerGenerated]
    [Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744")]
    [TypeIdentifier]
    public interface IDTExtensibility2
    {
        [DispId(1)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void OnConnection(
            [In, MarshalAs(UnmanagedType.IDispatch)] object Application,
            [In] ext_ConnectMode ConnectMode,
            [In, MarshalAs(UnmanagedType.IDispatch)] object AddInInst,
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(2)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void OnDisconnection(
            [In] ext_DisconnectMode RemoveMode,
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(3)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void OnAddInsUpdate(
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(4)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void OnStartupComplete(
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(5)]
        [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        void OnBeginShutdown(
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);
    }
}
