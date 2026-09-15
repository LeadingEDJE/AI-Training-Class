/** A client's derived status, computed client-side from its assignment list on every render. */
export type ClientStatus = 'Active' | 'Inactive' | 'Former';

/** An assignment's derived currency — shares the same underlying enum as {@link ClientStatus} on the server. */
export type AssignmentStatus = 'Active' | 'Inactive';
