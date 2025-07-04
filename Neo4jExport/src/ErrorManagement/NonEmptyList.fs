namespace Neo4jExport

/// A list that is guaranteed to have at least one element
type NonEmptyList<'T> = NonEmptyList of head: 'T * tail: 'T list

module NonEmptyList =
    /// Create a NonEmptyList with a single element
    let singleton x = NonEmptyList(x, [])

    /// Add an element to the front of the list
    let cons x (NonEmptyList(h, t)) = NonEmptyList(x, h :: t)

    /// Convert to a regular list
    let toList (NonEmptyList(h, t)) = h :: t

    /// Get the head element
    let head (NonEmptyList(h, _)) = h

    /// Get the tail as an option
    let tail (NonEmptyList(_, t)) =
        match t with
        | [] -> None
        | h :: t -> Some(NonEmptyList(h, t))

    /// Map a function over the list
    let map f (NonEmptyList(h, t)) = NonEmptyList(f h, List.map f t)

    /// Append two NonEmptyLists
    let append (NonEmptyList(h1, t1)) (NonEmptyList(h2, t2)) = NonEmptyList(h1, t1 @ (h2 :: t2))

    /// Create from a regular list
    let ofList =
        function
        | [] -> None
        | h :: t -> Some(NonEmptyList(h, t))
