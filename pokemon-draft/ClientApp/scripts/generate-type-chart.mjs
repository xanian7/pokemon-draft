import { writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { Dex } from '@pkmn/dex'

const types = Dex.types
  .all()
  .filter((type) => type.exists && !type.isNonstandard && type.name !== 'Stellar')

const chart = {}
for (const attack of types) {
  const attackId = attack.name.toLowerCase()
  chart[attackId] = {}

  for (const defense of types) {
    const defenseId = defense.name.toLowerCase()
    const multiplier = Dex.getImmunity(attack.name, [defense.name])
      ? 2 ** Dex.getEffectiveness(attack.name, [defense.name])
      : 0

    if (multiplier !== 1) chart[attackId][defenseId] = multiplier
  }
}

const output = `// Generated from @pkmn/dex. Run \`npm run generate:type-chart\` to refresh.\n\n` +
  `export const ATTACK_TYPES = ${JSON.stringify(types.map((type) => type.name.toLowerCase()))} as const\n\n` +
  `export const TYPE_CHART: Record<string, Record<string, number>> = ${JSON.stringify(chart, null, 2)}\n`

const outputUrl = new URL('../src/data/typeChart.ts', import.meta.url)
await writeFile(fileURLToPath(outputUrl), output)
