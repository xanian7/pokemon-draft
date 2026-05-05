<script setup lang="ts">
withDefaults(defineProps<{ path: string; size?: number }>(), { size: 20 })
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="currentColor"
    aria-hidden="true"
    style="display: inline-block; vertical-align: middle; flex-shrink: 0"
  >
    <path :d="path" />
  </svg>
</template>
