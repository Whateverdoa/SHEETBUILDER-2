import { cn } from '../../utils'

interface ProgressProps {
  value: number
  max?: number
  size?: 'sm' | 'md' | 'lg'
  variant?: 'primary' | 'secondary' | 'success' | 'warning' | 'error'
  showLabel?: boolean
  label?: string
  className?: string
}

const progressSizes = {
  sm: 'h-1',
  md: 'h-2',
  lg: 'h-3',
}

const progressVariants = {
  primary: 'progress-fill',
  secondary: 'bg-gradient-to-r from-secondary-500 to-secondary-700',
  success: 'bg-gradient-to-r from-green-500 to-green-600',
  warning: 'bg-gradient-to-r from-yellow-500 to-yellow-600',
  error: 'bg-gradient-to-r from-red-500 to-red-600',
}

export function Progress({
  value,
  max = 100,
  size = 'md',
  variant = 'primary',
  showLabel = false,
  label,
  className,
}: ProgressProps) {
  const percentage = Math.min(Math.max((value / max) * 100, 0), 100)

  return (
    <div className={cn('space-y-1', className)}>
      {(showLabel || label) && (
        <div className="flex justify-between items-center text-sm">
          <span className="text-slate-700 dark:text-slate-300">
            {label || 'Progress'}
          </span>
          {showLabel && (
            <span className="text-slate-500 dark:text-slate-400">
              {Math.round(percentage)}%
            </span>
          )}
        </div>
      )}
      
      <div className={cn('progress-bar', progressSizes[size])}>
        <div
          className={cn(progressVariants[variant])}
          style={{ width: `${percentage}%` }}
        />
      </div>
    </div>
  )
}