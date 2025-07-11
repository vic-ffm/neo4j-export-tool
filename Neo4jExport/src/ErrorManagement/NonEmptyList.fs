// MIT License
//
// Copyright (c) 2025-present State Government of Victoria
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace Neo4jExport

/// A list that is guaranteed to have at least one element
/// This type makes illegal states unrepresentable - you cannot create an empty NonEmptyList.
/// The compiler enforces that error collections always contain at least one error.
type NonEmptyList<'T> = NonEmptyList of head: 'T * tail: 'T list

module NonEmptyList =
    let singleton x = NonEmptyList(x, [])

    // Prepend element to the front - old head becomes part of tail
    let cons x (NonEmptyList(h, t)) = NonEmptyList(x, h :: t)

    let toList (NonEmptyList(h, t)) = h :: t

    let head (NonEmptyList(h, _)) = h

    /// Get the tail as an option
    let tail (NonEmptyList(_, t)) =
        match t with
        | [] -> None
        | h :: t -> Some(NonEmptyList(h, t))

    let map f (NonEmptyList(h, t)) = NonEmptyList(f h, List.map f t)

    // Append two non-empty lists - @ is F#'s list concatenation operator
    // Result preserves the guarantee of non-emptiness
    let append (NonEmptyList(h1, t1)) (NonEmptyList(h2, t2)) = NonEmptyList(h1, t1 @ (h2 :: t2))

    let ofList =
        function
        | [] -> None
        | h :: t -> Some(NonEmptyList(h, t))
