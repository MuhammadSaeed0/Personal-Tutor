/** Periodic table data and a deterministic chemical-equation balancer. */

export interface Element {
  z: number; symbol: string; en: string; ar: string; mass: number; period: number; group: number; category: string;
}

// z|symbol|English|Arabic|mass|period|group|category  (group 0 = f-block row)
const RAW = `1|H|Hydrogen|هيدروجين|1.008|1|1|nonmetal
2|He|Helium|هيليوم|4.0026|1|18|noble
3|Li|Lithium|ليثيوم|6.94|2|1|alkali
4|Be|Beryllium|بيريليوم|9.0122|2|2|alkaline
5|B|Boron|بورون|10.81|2|13|metalloid
6|C|Carbon|كربون|12.011|2|14|nonmetal
7|N|Nitrogen|نيتروجين|14.007|2|15|nonmetal
8|O|Oxygen|أكسجين|15.999|2|16|nonmetal
9|F|Fluorine|فلور|18.998|2|17|halogen
10|Ne|Neon|نيون|20.180|2|18|noble
11|Na|Sodium|صوديوم|22.990|3|1|alkali
12|Mg|Magnesium|مغنيسيوم|24.305|3|2|alkaline
13|Al|Aluminium|ألومنيوم|26.982|3|13|post
14|Si|Silicon|سيليكون|28.085|3|14|metalloid
15|P|Phosphorus|فوسفور|30.974|3|15|nonmetal
16|S|Sulfur|كبريت|32.06|3|16|nonmetal
17|Cl|Chlorine|كلور|35.45|3|17|halogen
18|Ar|Argon|أرغون|39.948|3|18|noble
19|K|Potassium|بوتاسيوم|39.098|4|1|alkali
20|Ca|Calcium|كالسيوم|40.078|4|2|alkaline
21|Sc|Scandium|سكانديوم|44.956|4|3|transition
22|Ti|Titanium|تيتانيوم|47.867|4|4|transition
23|V|Vanadium|فاناديوم|50.942|4|5|transition
24|Cr|Chromium|كروم|51.996|4|6|transition
25|Mn|Manganese|منغنيز|54.938|4|7|transition
26|Fe|Iron|حديد|55.845|4|8|transition
27|Co|Cobalt|كوبالت|58.933|4|9|transition
28|Ni|Nickel|نيكل|58.693|4|10|transition
29|Cu|Copper|نحاس|63.546|4|11|transition
30|Zn|Zinc|خارصين|65.38|4|12|transition
31|Ga|Gallium|غاليوم|69.723|4|13|post
32|Ge|Germanium|جرمانيوم|72.630|4|14|metalloid
33|As|Arsenic|زرنيخ|74.922|4|15|metalloid
34|Se|Selenium|سيلينيوم|78.971|4|16|nonmetal
35|Br|Bromine|بروم|79.904|4|17|halogen
36|Kr|Krypton|كريبتون|83.798|4|18|noble
37|Rb|Rubidium|روبيديوم|85.468|5|1|alkali
38|Sr|Strontium|سترونشيوم|87.62|5|2|alkaline
39|Y|Yttrium|إتريوم|88.906|5|3|transition
40|Zr|Zirconium|زركونيوم|91.224|5|4|transition
41|Nb|Niobium|نيوبيوم|92.906|5|5|transition
42|Mo|Molybdenum|موليبدنوم|95.95|5|6|transition
43|Tc|Technetium|تكنيشيوم|98|5|7|transition
44|Ru|Ruthenium|روثينيوم|101.07|5|8|transition
45|Rh|Rhodium|روديوم|102.91|5|9|transition
46|Pd|Palladium|بالاديوم|106.42|5|10|transition
47|Ag|Silver|فضة|107.87|5|11|transition
48|Cd|Cadmium|كادميوم|112.41|5|12|transition
49|In|Indium|إنديوم|114.82|5|13|post
50|Sn|Tin|قصدير|118.71|5|14|post
51|Sb|Antimony|إثمد|121.76|5|15|metalloid
52|Te|Tellurium|تيلوريوم|127.60|5|16|metalloid
53|I|Iodine|يود|126.90|5|17|halogen
54|Xe|Xenon|زينون|131.29|5|18|noble
55|Cs|Caesium|سيزيوم|132.91|6|1|alkali
56|Ba|Barium|باريوم|137.33|6|2|alkaline
57|La|Lanthanum|لانثانوم|138.91|6|3|lanthanide
58|Ce|Cerium|سيريوم|140.12|6|0|lanthanide
59|Pr|Praseodymium|براسيوديميوم|140.91|6|0|lanthanide
60|Nd|Neodymium|نيوديميوم|144.24|6|0|lanthanide
61|Pm|Promethium|بروميثيوم|145|6|0|lanthanide
62|Sm|Samarium|ساماريوم|150.36|6|0|lanthanide
63|Eu|Europium|يوروبيوم|151.96|6|0|lanthanide
64|Gd|Gadolinium|غادولينيوم|157.25|6|0|lanthanide
65|Tb|Terbium|تيربيوم|158.93|6|0|lanthanide
66|Dy|Dysprosium|ديسبروسيوم|162.50|6|0|lanthanide
67|Ho|Holmium|هولميوم|164.93|6|0|lanthanide
68|Er|Erbium|إربيوم|167.26|6|0|lanthanide
69|Tm|Thulium|ثوليوم|168.93|6|0|lanthanide
70|Yb|Ytterbium|إتيربيوم|173.05|6|0|lanthanide
71|Lu|Lutetium|لوتيشيوم|174.97|6|0|lanthanide
72|Hf|Hafnium|هافنيوم|178.49|6|4|transition
73|Ta|Tantalum|تنتالوم|180.95|6|5|transition
74|W|Tungsten|تنغستن|183.84|6|6|transition
75|Re|Rhenium|رينيوم|186.21|6|7|transition
76|Os|Osmium|أوزميوم|190.23|6|8|transition
77|Ir|Iridium|إيريديوم|192.22|6|9|transition
78|Pt|Platinum|بلاتين|195.08|6|10|transition
79|Au|Gold|ذهب|196.97|6|11|transition
80|Hg|Mercury|زئبق|200.59|6|12|transition
81|Tl|Thallium|ثاليوم|204.38|6|13|post
82|Pb|Lead|رصاص|207.2|6|14|post
83|Bi|Bismuth|بزموت|208.98|6|15|post
84|Po|Polonium|بولونيوم|209|6|16|post
85|At|Astatine|أستاتين|210|6|17|halogen
86|Rn|Radon|رادون|222|6|18|noble
87|Fr|Francium|فرانسيوم|223|7|1|alkali
88|Ra|Radium|راديوم|226|7|2|alkaline
89|Ac|Actinium|أكتينيوم|227|7|3|actinide
90|Th|Thorium|ثوريوم|232.04|7|0|actinide
91|Pa|Protactinium|بروتكتينيوم|231.04|7|0|actinide
92|U|Uranium|يورانيوم|238.03|7|0|actinide
93|Np|Neptunium|نبتونيوم|237|7|0|actinide
94|Pu|Plutonium|بلوتونيوم|244|7|0|actinide
95|Am|Americium|أمريسيوم|243|7|0|actinide
96|Cm|Curium|كوريوم|247|7|0|actinide
97|Bk|Berkelium|بركيليوم|247|7|0|actinide
98|Cf|Californium|كاليفورنيوم|251|7|0|actinide
99|Es|Einsteinium|أينشتاينيوم|252|7|0|actinide
100|Fm|Fermium|فرميوم|257|7|0|actinide
101|Md|Mendelevium|مندليفيوم|258|7|0|actinide
102|No|Nobelium|نوبليوم|259|7|0|actinide
103|Lr|Lawrencium|لورنسيوم|266|7|0|actinide
104|Rf|Rutherfordium|رذرفورديوم|267|7|4|transition
105|Db|Dubnium|دوبنيوم|268|7|5|transition
106|Sg|Seaborgium|سيبورغيوم|269|7|6|transition
107|Bh|Bohrium|بوريوم|270|7|7|transition
108|Hs|Hassium|هاسيوم|277|7|8|transition
109|Mt|Meitnerium|مايتنريوم|278|7|9|unknown
110|Ds|Darmstadtium|دارمشتاتيوم|281|7|10|unknown
111|Rg|Roentgenium|رونتجينيوم|282|7|11|unknown
112|Cn|Copernicium|كوبرنيسيوم|285|7|12|unknown
113|Nh|Nihonium|نيهونيوم|286|7|13|unknown
114|Fl|Flerovium|فليروفيوم|289|7|14|unknown
115|Mc|Moscovium|موسكوفيوم|290|7|15|unknown
116|Lv|Livermorium|ليفرموريوم|293|7|16|unknown
117|Ts|Tennessine|تينيسين|294|7|17|unknown
118|Og|Oganesson|أوغانيسون|294|7|18|unknown`;

export const ELEMENTS: Element[] = RAW.split('\n').map(line => {
  const [z, symbol, en, ar, mass, period, group, category] = line.split('|');
  return { z: +z, symbol, en, ar, mass: +mass, period: +period, group: +group, category };
});

export const ELEMENT_SYMBOLS = new Set(ELEMENTS.map(e => e.symbol));

// ---- Formula parsing ----------------------------------------------------------------------------------

/** Parses formulas such as Ca(OH)2, Fe2(SO4)3 or CuSO4·5H2O into element counts. */
export function parseFormula(formula: string): Map<string, number> {
  const parts = formula.replace(/\s+/g, '').split(/[·.*]/);
  const total = new Map<string, number>();
  for (const part of parts) {
    const lead = /^(\d+)/.exec(part);
    const multiplier = lead ? Number(lead[1]) : 1;
    const counts = parseGroup(part.slice(lead ? lead[1].length : 0));
    for (const [el, n] of counts) total.set(el, (total.get(el) ?? 0) + n * multiplier);
  }
  return total;
}

function parseGroup(s: string): Map<string, number> {
  const stack: Map<string, number>[] = [new Map()];
  let i = 0;
  while (i < s.length) {
    const c = s[i];
    if (c === '(' || c === '[') { stack.push(new Map()); i++; continue; }
    if (c === ')' || c === ']') {
      i++;
      const num = readNumber(s, i);
      i += num.length;
      const group = stack.pop();
      if (!group || stack.length === 0) throw new Error('unbalanced');
      const k = num ? Number(num) : 1;
      for (const [el, n] of group) stack[stack.length - 1].set(el, (stack[stack.length - 1].get(el) ?? 0) + n * k);
      continue;
    }
    const m = /^[A-Z][a-z]?/.exec(s.slice(i));
    if (!m) throw new Error(`unexpected:${c}`);
    if (!ELEMENT_SYMBOLS.has(m[0])) throw new Error(`element:${m[0]}`);
    i += m[0].length;
    const num = readNumber(s, i);
    i += num.length;
    const top = stack[stack.length - 1];
    top.set(m[0], (top.get(m[0]) ?? 0) + (num ? Number(num) : 1));
  }
  if (stack.length !== 1) throw new Error('unbalanced');
  return stack[0];
}

function readNumber(s: string, i: number): string {
  const m = /^\d+/.exec(s.slice(i));
  return m ? m[0] : '';
}

export function molarMass(formula: string): number {
  let sum = 0;
  for (const [el, n] of parseFormula(formula)) sum += (ELEMENTS.find(e => e.symbol === el)?.mass ?? 0) * n;
  return sum;
}

// ---- Balancing (exact rational Gaussian elimination) ---------------------------------------------------

type Frac = [bigint, bigint];
const gcd = (a: bigint, b: bigint): bigint => { a = a < 0n ? -a : a; b = b < 0n ? -b : b; while (b) [a, b] = [b, a % b]; return a; };
const norm = ([n, d]: Frac): Frac => { if (d < 0n) { n = -n; d = -d; } const g = gcd(n, d) || 1n; return [n / g, d / g]; };
const sub = (a: Frac, b: Frac): Frac => norm([a[0] * b[1] - b[0] * a[1], a[1] * b[1]]);
const mul = (a: Frac, b: Frac): Frac => norm([a[0] * b[0], a[1] * b[1]]);
const div = (a: Frac, b: Frac): Frac => norm([a[0] * b[1], a[1] * b[0]]);

export interface BalanceResult { reactants: string[]; products: string[]; coefficients: number[]; }

/** Balances "A + B -> C + D" (also accepts = and →). Throws with a code on invalid input or no unique solution. */
export function balance(equation: string): BalanceResult {
  const [lhs, rhs] = equation.split(/->|→|=|⟶/).map(x => x?.trim());
  if (!lhs || !rhs) throw new Error('no_arrow');
  const reactants = lhs.split('+').map(s => s.trim()).filter(Boolean);
  const products = rhs.split('+').map(s => s.trim()).filter(Boolean);
  const species = [...reactants, ...products];
  const parsed = species.map(parseFormula);
  const elements = [...new Set(parsed.flatMap(p => [...p.keys()]))];

  // Rows: elements; columns: species (products negative).
  const m: Frac[][] = elements.map(el => species.map((_, j) => [BigInt((parsed[j].get(el) ?? 0) * (j < reactants.length ? 1 : -1)), 1n] as Frac));
  const cols = species.length;
  const pivots: number[] = [];
  let row = 0;
  for (let col = 0; col < cols && row < m.length; col++) {
    const p = m.findIndex((r, i) => i >= row && r[col][0] !== 0n);
    if (p < 0) continue;
    [m[row], m[p]] = [m[p], m[row]];
    const pv = m[row][col];
    m[row] = m[row].map(v => div(v, pv));
    for (let i = 0; i < m.length; i++) {
      if (i === row || m[i][col][0] === 0n) continue;
      const f = m[i][col];
      m[i] = m[i].map((v, j) => sub(v, mul(f, m[row][j])));
    }
    pivots.push(col);
    row++;
  }
  const free = Array.from({ length: cols }, (_, i) => i).filter(i => !pivots.includes(i));
  if (free.length !== 1) throw new Error(free.length === 0 ? 'impossible' : 'ambiguous');

  const f = free[0];
  const solution: Frac[] = Array.from({ length: cols }, () => [0n, 1n] as Frac);
  solution[f] = [1n, 1n];
  pivots.forEach((col, r) => { solution[col] = norm([-m[r][f][0], m[r][f][1]]); });
  const lcm = solution.reduce((acc, [, d]) => (acc * d) / gcd(acc, d), 1n);
  let ints = solution.map(([n, d]) => (n * lcm) / d);
  const g = ints.reduce((a, b) => gcd(a, b), 0n) || 1n;
  ints = ints.map(v => v / g);
  if (ints.some(v => v <= 0n)) {
    if (ints.every(v => v < 0n)) ints = ints.map(v => -v);
    else throw new Error('impossible');
  }
  return { reactants, products, coefficients: ints.map(Number) };
}

export function formatEquation(r: BalanceResult): string {
  const term = (c: number, s: string) => (c === 1 ? s : `${c}${s}`);
  return `${r.reactants.map((s, i) => term(r.coefficients[i], s)).join(' + ')} -> ${r.products.map((s, i) => term(r.coefficients[r.reactants.length + i], s)).join(' + ')}`;
}
