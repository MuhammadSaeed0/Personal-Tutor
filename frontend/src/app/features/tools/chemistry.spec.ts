import { ELEMENTS, balance, formatEquation, molarMass, parseFormula } from './chemistry';

describe('chemistry tools', () => {
  it('contains all 118 elements in order', () => {
    expect(ELEMENTS.length).toBe(118);
    expect(ELEMENTS.map(e => e.z)).toEqual(Array.from({ length: 118 }, (_, i) => i + 1));
    expect(ELEMENTS.find(e => e.symbol === 'Fe')?.ar).toBe('حديد');
  });

  it('parses formulas with brackets and hydrates', () => {
    expect(Object.fromEntries(parseFormula('Ca(OH)2'))).toEqual({ Ca: 1, O: 2, H: 2 });
    expect(Object.fromEntries(parseFormula('Fe2(SO4)3'))).toEqual({ Fe: 2, S: 3, O: 12 });
    expect(Object.fromEntries(parseFormula('CuSO4·5H2O'))).toEqual({ Cu: 1, S: 1, O: 9, H: 10 });
    expect(() => parseFormula('Xx2')).toThrow(/element/);
    expect(() => parseFormula('Ca(OH2')).toThrow();
  });

  it('computes molar masses', () => {
    expect(molarMass('H2O')).toBeCloseTo(18.015, 2);
    expect(molarMass('NaCl')).toBeCloseTo(58.44, 1);
  });

  it('balances equations with exact integer coefficients', () => {
    expect(balance('Fe + O2 -> Fe2O3').coefficients).toEqual([4, 3, 2]);
    expect(balance('C3H8 + O2 -> CO2 + H2O').coefficients).toEqual([1, 5, 3, 4]);
    expect(balance('KMnO4 + HCl -> KCl + MnCl2 + H2O + Cl2').coefficients).toEqual([2, 16, 2, 2, 8, 5]);
    expect(formatEquation(balance('H2 + O2 = H2O'))).toBe('2H2 + O2 -> 2H2O');
  });

  it('reports equations that cannot be balanced', () => {
    expect(() => balance('H2 + O2')).toThrow('no_arrow');
    expect(() => balance('H2 -> O2')).toThrow('impossible');
  });
});
