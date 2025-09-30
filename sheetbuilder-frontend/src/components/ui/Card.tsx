import type { CardProps } from '../../types'
import { cn } from '../../utils'

export function Card({ 
  children, 
  className, 
  hover = false, 
  glass = false,
  ...props 
}: CardProps & React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        // Base card styles
        'card',
        // Conditional styles
        {
          'card-hover': hover,
          'glass': glass,
        },
        className
      )}
      {...props}
    >
      {children}
    </div>
  )
}

export function CardHeader({ 
  children, 
  className, 
  ...props 
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('px-6 py-4 border-b border-slate-200 dark:border-slate-700', className)}
      {...props}
    >
      {children}
    </div>
  )
}

export function CardContent({ 
  children, 
  className, 
  ...props 
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('px-6 py-4', className)} {...props}>
      {children}
    </div>
  )
}

export function CardFooter({ 
  children, 
  className, 
  ...props 
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('px-6 py-4 border-t border-slate-200 dark:border-slate-700', className)}
      {...props}
    >
      {children}
    </div>
  )
}