// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as ILRepack from "Sdk.Managed.Tools.ILRepack";
import * as Shared from "Sdk.Managed.Shared";

namespace UtilitiesCore {
    export declare const qualifier : BuildXLSdk.AllSupportedQualifiers;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.UtilitiesCore",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Runtime.Serialization.dll,
                NetFx.System.Xml.dll,
            ]),
        ],
        allowUnsafeBlocks: true,
    });
}
