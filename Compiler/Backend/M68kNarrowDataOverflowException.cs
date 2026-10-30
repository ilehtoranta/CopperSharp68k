/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

// Signals a speculative private-table encoding failure, not a source error.
internal sealed class M68kNarrowDataOverflowException : Exception;
