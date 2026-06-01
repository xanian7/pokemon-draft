export interface NavigationButton {
  label: string
  icon: string
  route: string
  requiresAuth: boolean
  adminOnly?: boolean
}
