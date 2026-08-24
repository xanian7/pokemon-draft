<script setup lang="ts">
import { Chart, type ChartConfiguration, type ChartDataset } from 'chart.js/auto'
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

export interface PointsProgressionSeries {
  playerId: string
  label: string
  values: number[]
}

const props = defineProps<{
  series: PointsProgressionSeries[]
  currentPlayerId: string | null
}>()

const canvas = ref<HTMLCanvasElement | null>(null)
let chart: Chart<'line'> | null = null

const colors = ['#7c6cff', '#2ab6ff', '#35d39a', '#ffca62', '#ff5c7a', '#c084fc', '#fb923c', '#22d3ee']

function cssColor(name: string, fallback: string) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback
}

function createConfig(): ChartConfiguration<'line'> {
  const weekCount = props.series[0]?.values.length ?? 0
  const datasets: ChartDataset<'line'>[] = props.series.map((player, index) => {
    const color = colors[index % colors.length]
    const isCurrentPlayer = player.playerId === props.currentPlayerId

    return {
      label: player.label,
      data: player.values,
      borderColor: color,
      backgroundColor: color,
      borderWidth: isCurrentPlayer ? 4 : 2.5,
      pointRadius: isCurrentPlayer ? 4 : 3,
      pointHoverRadius: 6,
      tension: 0.28,
    }
  })

  return {
    type: 'line',
    data: {
      labels: Array.from({ length: weekCount }, (_, index) => `Week ${index + 1}`),
      datasets,
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: {
        intersect: false,
        mode: 'index',
      },
      plugins: {
        legend: {
          position: 'bottom',
          labels: {
            boxHeight: 3,
            boxWidth: 20,
            color: cssColor('--text-muted', '#9aa4bd'),
            padding: 16,
            usePointStyle: false,
          },
        },
        tooltip: {
          backgroundColor: cssColor('--card-bg-solid', '#141a2b'),
          borderColor: cssColor('--border-color', '#383838'),
          borderWidth: 1,
          titleColor: cssColor('--text', '#f4f6ff'),
          bodyColor: cssColor('--text-muted', '#9aa4bd'),
          padding: 12,
        },
      },
      scales: {
        x: {
          grid: {
            color: 'rgba(154, 164, 189, 0.08)',
          },
          ticks: {
            color: cssColor('--text-muted', '#9aa4bd'),
          },
          title: {
            display: true,
            text: 'Week',
            color: cssColor('--text-muted', '#9aa4bd'),
          },
        },
        y: {
          beginAtZero: true,
          grid: {
            color: 'rgba(154, 164, 189, 0.12)',
          },
          ticks: {
            color: cssColor('--text-muted', '#9aa4bd'),
            precision: 0,
          },
          title: {
            display: true,
            text: 'Points',
            color: cssColor('--text-muted', '#9aa4bd'),
          },
        },
      },
    },
  }
}

async function renderChart() {
  await nextTick()
  if (!canvas.value) return

  chart?.destroy()
  chart = new Chart(canvas.value, createConfig())
}

onMounted(renderChart)
watch(() => [props.series, props.currentPlayerId], renderChart, { deep: true })
onBeforeUnmount(() => chart?.destroy())
</script>

<template>
  <div class="points-progression-chart">
    <canvas ref="canvas" aria-label="Player points by week" role="img" />
  </div>
</template>

<style scoped>
.points-progression-chart {
  position: relative;
  width: 100%;
  height: clamp(280px, 36vw, 430px);
}

@media (max-width: 600px) {
  .points-progression-chart {
    height: 320px;
  }
}
</style>
