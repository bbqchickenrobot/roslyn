﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.ExtractMethod;

namespace Microsoft.VisualStudio.LanguageServices.CSharp.Options
{
    public partial class AutomationObject
    {
        public int ExtractMethod_AllowBestEffort
        {
            get { return GetBooleanOption(ExtractMethodPresentationOptions.AllowBestEffort); }
            set { SetBooleanOption(ExtractMethodPresentationOptions.AllowBestEffort, value); }
        }

        public int ExtractMethod_DoNotPutOutOrRefOnStruct
        {
            get { return GetBooleanOption(ExtractMethodOptions.Metadata.DontPutOutOrRefOnStruct); }
            set { SetBooleanOption(ExtractMethodOptions.Metadata.DontPutOutOrRefOnStruct, value); }
        }
    }
}
