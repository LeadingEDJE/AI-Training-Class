import { buildTeamDirectoryQuery } from '../../../../src/features/team-directory/useTeamDirectory';

describe('buildTeamDirectoryQuery', () => {
  it('returns an empty string when nothing is constrained', () => {
    expect(buildTeamDirectoryQuery({})).toBe('');
  });

  it('omits blank and whitespace-only values', () => {
    // `?search=` and no `search` at all must mean the same thing to the server.
    expect(buildTeamDirectoryQuery({ search: '   ', employeeType: '', state: undefined })).toBe('');
  });

  it('trims values it does send', () => {
    expect(buildTeamDirectoryQuery({ search: '  mul  ' })).toBe('?search=mul');
  });

  it('includes every AC-6 control', () => {
    const query = buildTeamDirectoryQuery({
      search: 'smi',
      employeeType: 'Full Time',
      state: 'OH',
      coachId: '9',
      skill: '3',
      sort: 'lastName',
      desc: true,
      status: 'all',
    });

    expect(query).toContain('search=smi');
    expect(query).toContain('employeeType=Full+Time');
    expect(query).toContain('state=OH');
    expect(query).toContain('coachId=9');
    expect(query).toContain('skill=3');
    expect(query).toContain('sort=lastName');
    expect(query).toContain('desc=true');
    expect(query).toContain('status=all');
  });

  it('omits the coach filter when blank, issue #655', () => {
    expect(buildTeamDirectoryQuery({ coachId: '' })).toBe('');
  });

  it('omits the skill filter when blank', () => {
    expect(buildTeamDirectoryQuery({ skill: '' })).toBe('');
  });

  it('omits desc entirely when ascending', () => {
    expect(buildTeamDirectoryQuery({ sort: 'lastName', desc: false })).toBe('?sort=lastName');
  });
});
