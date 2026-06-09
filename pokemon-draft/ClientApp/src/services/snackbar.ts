import { readonly, ref } from 'vue'

export type SnackbarColor = 'error' | 'info' | 'success' | 'warning'

export interface SnackbarMessage {
  id: number
  text: string
  color: SnackbarColor
  timeout: number
}

const queue = ref<SnackbarMessage[]>([])
let nextId = 1

export function enqueueSnackbar(
  text: string,
  color: SnackbarColor = 'info',
  timeout = 5000,
) {
  queue.value.push({ id: nextId++, text, color, timeout })
}

export function dismissSnackbar(id: number) {
  queue.value = queue.value.filter((message) => message.id !== id)
}

export function useSnackbarQueue() {
  return {
    queue: readonly(queue),
    enqueueSnackbar,
    dismissSnackbar,
  }
}
