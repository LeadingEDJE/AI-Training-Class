/** One residence option: what the form shows, and what it submits. */
export interface UsState {
  /** The USPS two-letter code — what `compass.employee.state_of_residence` holds. */
  code: string;
  /** The full name — what a person reads in the select. */
  name: string;
}

/**
 * The 50 US states plus the District of Columbia, ordered by name.
 *
 * **`api/Modules/Compass/UsStateCodes.cs` is the authority, not this file.** That array generates the
 * database `CHECK` on `compass.employee.state_of_residence` and the service validates against it
 * (FR-014). This is the presentation half of the same vocabulary: names because "Ohio" is what a human
 * reads, codes because `char(2)` is what the column stores.
 *
 * A drift between the two is the worst kind of defect available here — a 400 on a value the form itself
 * offered, where the administrator did nothing wrong and has no way to tell. `us-states.test.ts` pins the
 * count at 51 to match `UsStateCodesTests`, and asserts the territories stay out.
 *
 * **US territories (PR, GU, VI, AS, MP) are excluded deliberately**, not overlooked: AC-NFR-6 supports
 * only US-based EDJErs and the ERD says "US state code". Adding one here without adding it to
 * `UsStateCodes.All` produces exactly the mismatch above; adding it to both is an additive migration,
 * because the `CHECK` is generated from that array.
 *
 * Ordered by NAME rather than by code, because a list running AL, AK, AZ is alphabetical by an
 * abbreviation the reader is not looking at.
 */
export const US_STATES: readonly UsState[] = [
  { code: 'AL', name: 'Alabama' },
  { code: 'AK', name: 'Alaska' },
  { code: 'AZ', name: 'Arizona' },
  { code: 'AR', name: 'Arkansas' },
  { code: 'CA', name: 'California' },
  { code: 'CO', name: 'Colorado' },
  { code: 'CT', name: 'Connecticut' },
  { code: 'DE', name: 'Delaware' },
  { code: 'DC', name: 'District of Columbia' },
  { code: 'FL', name: 'Florida' },
  { code: 'GA', name: 'Georgia' },
  { code: 'HI', name: 'Hawaii' },
  { code: 'ID', name: 'Idaho' },
  { code: 'IL', name: 'Illinois' },
  { code: 'IN', name: 'Indiana' },
  { code: 'IA', name: 'Iowa' },
  { code: 'KS', name: 'Kansas' },
  { code: 'KY', name: 'Kentucky' },
  { code: 'LA', name: 'Louisiana' },
  { code: 'ME', name: 'Maine' },
  { code: 'MD', name: 'Maryland' },
  { code: 'MA', name: 'Massachusetts' },
  { code: 'MI', name: 'Michigan' },
  { code: 'MN', name: 'Minnesota' },
  { code: 'MS', name: 'Mississippi' },
  { code: 'MO', name: 'Missouri' },
  { code: 'MT', name: 'Montana' },
  { code: 'NE', name: 'Nebraska' },
  { code: 'NV', name: 'Nevada' },
  { code: 'NH', name: 'New Hampshire' },
  { code: 'NJ', name: 'New Jersey' },
  { code: 'NM', name: 'New Mexico' },
  { code: 'NY', name: 'New York' },
  { code: 'NC', name: 'North Carolina' },
  { code: 'ND', name: 'North Dakota' },
  { code: 'OH', name: 'Ohio' },
  { code: 'OK', name: 'Oklahoma' },
  { code: 'OR', name: 'Oregon' },
  { code: 'PA', name: 'Pennsylvania' },
  { code: 'RI', name: 'Rhode Island' },
  { code: 'SC', name: 'South Carolina' },
  { code: 'SD', name: 'South Dakota' },
  { code: 'TN', name: 'Tennessee' },
  { code: 'TX', name: 'Texas' },
  { code: 'UT', name: 'Utah' },
  { code: 'VT', name: 'Vermont' },
  { code: 'VA', name: 'Virginia' },
  { code: 'WA', name: 'Washington' },
  { code: 'WV', name: 'West Virginia' },
  { code: 'WI', name: 'Wisconsin' },
  { code: 'WY', name: 'Wyoming' },
];
