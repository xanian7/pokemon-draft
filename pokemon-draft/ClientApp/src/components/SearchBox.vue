<script setup lang="ts">
import { ref, watch } from 'vue'
import AppIcon from './AppIcon.vue'
import { mdiMagnify } from '@mdi/js'

const props = defineProps<{
    modelValue: string
}>()

const emit = defineEmits<{
    (e: 'update:modelValue', value: string): void
}>()

const searchQuery = ref(props.modelValue)
watch(searchQuery, (newValue) => {
    emit('update:modelValue', newValue)
})
    
</script>

<template>
    <form class="search-box" @submit.prevent>
        <AppIcon class="search-box__icon" :path="mdiMagnify" :size="18" />
        <input v-model="searchQuery" placeholder="Search Pokémon..."/>
    </form>
</template>