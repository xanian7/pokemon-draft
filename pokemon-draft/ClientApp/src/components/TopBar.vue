<script setup lang="ts">
import MasterBall from '../../public/Master-Ball.png'
import ProfilePopup from '@/components/ProfilePopup.vue'
</script>

<template>
  <v-app-bar app class="top-bar" flat>
    <div class="top-bar__inner">
      <div class="brand">
        <span class="brand__mark">
          <v-img :src="MasterBall" width="34" height="34" />
        </span>
        <span class="brand__copy">
          <strong>PokéDraft</strong>
          <small>League command center</small>
        </span>
      </div>
      <div class="profile">
        <ProfilePopup />
      </div>
    </div>
  </v-app-bar>
</template>

<style scoped>
.top-bar {
  background: rgba(8, 11, 20, 0.96) !important;
  border-bottom: 1px solid var(--border-color) !important;
}

.top-bar__inner {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 18px;
}

.brand {
  display: flex;
  align-items: center;
  gap: 11px;
}

.brand__mark {
  width: 42px;
  height: 42px;
  display: grid;
  place-items: center;
  border-radius: 13px;
  background: linear-gradient(145deg, rgba(var(--primary-rgb), 0.24), rgba(42, 182, 255, 0.1));
  border: 1px solid rgba(var(--primary-rgb), 0.26);
  box-shadow: inset 0 1px rgba(255, 255, 255, 0.08);
}

.brand__copy {
  display: flex;
  flex-direction: column;
  line-height: 1.15;
}

.brand__copy strong {
  font-size: 1.02rem;
  font-weight: 800;
  letter-spacing: -0.025em;
}

.brand__copy small {
  margin-top: 3px;
  color: var(--text-muted);
  font-size: 0.68rem;
  letter-spacing: 0.04em;
}

.profile {
  margin-right: -12px;
}

@media (max-width: 520px) {
  .top-bar__inner {
    padding-inline: 12px;
  }

  .brand__copy small {
    display: none;
  }

  .brand__mark {
    width: 38px;
    height: 38px;
    border-radius: 12px;
  }
}
</style>
